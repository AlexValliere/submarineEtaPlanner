using System.Numerics;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using MessagePack;
using SubmarineEtaPlanner.Planner;

namespace SubmarineEtaPlanner.SubmarineTrackerRuntime;

public sealed class DalamudSubmarineCatalog :
    ISubmarineCatalog,
    IRouteOperationalCatalog,
    IRouteSelectionCatalog,
    IRouteSearchDiagnostics,
    IPlannerDataDiagnostics
{
    private const int FixedVoyageTimeSeconds = 43200;
    private const int RouteSearchCacheLimit = 32768;
    private const long RouteRankingCacheMaximumBytes = 64L * 1024 * 1024;
    private readonly ExcelSheet<SubmarineExploration> explorationSheet;
    private readonly ExcelSheet<SubmarinePart> partSheet;
    private readonly ExcelSheet<SubmarineRank> rankSheet;
    private readonly Dictionary<uint, SubmarineExploration> sectorById;
    private readonly RouteOperationalCalculator routeOperationalCalculator;
    private readonly uint[] reversedMapStartSectors;
    private readonly CalculatedRouteData calculatedRoutes;
    private readonly RouteCandidateIndex routeIndex;
    private readonly IPluginLog log;
    private readonly uint lastRank;
    private readonly BoundedLruCache<RouteSearchCacheKey, CachedRoute> routeSearchCache =
        new(RouteSearchCacheLimit);
    private readonly BoundedLruCache<RouteRankingKey, int[]> routeRankingCache =
        new(int.MaxValue, RouteRankingCacheMaximumBytes, routeIds => (long)routeIds.Length * sizeof(int));
    private readonly Dictionary<int, long[]> durationTicksBySpeed = [];
    private readonly Dictionary<ExpProfile, uint[]> sectorExpByProfile = [];
    private readonly IReadOnlyList<string> plannerDataWarnings;
    private long routeQueries;
    private long routeCacheHits;
    private long routesEvaluated;
    private long rankingBuilds;
    private long rankingCacheHits;
    private long rankedRoutesEvaluated;
    private long exhaustiveRoutesEvaluated;
    private long rankingBuildMilliseconds;
    private long exactCacheEvictions;
    private long rankingCacheEvictions;

    public DalamudSubmarineCatalog(IDataManager dataManager, string pluginDirectory, IPluginLog log)
    {
        this.log = log;
        this.explorationSheet = dataManager.GetExcelSheet<SubmarineExploration>();
        this.partSheet = dataManager.GetExcelSheet<SubmarinePart>();
        this.rankSheet = dataManager.GetExcelSheet<SubmarineRank>();
        this.sectorById = this.explorationSheet.ToDictionary(row => row.RowId, row => row);
        this.routeOperationalCalculator = new RouteOperationalCalculator(
            this.sectorById.ToDictionary(
                pair => pair.Key,
                pair => checked((int)pair.Value.CeruleumTankReq)),
            CalculateDuration);
        this.reversedMapStartSectors = this.explorationSheet
            .Where(row => row.StartingPoint)
            .Select(row => row.RowId)
            .OrderDescending()
            .ToArray();
        RouteDestinations = this.explorationSheet
            .Where(row => !row.StartingPoint && row.ExpReward != 0)
            .Select(row =>
            {
                var name = PointName(row.RowId);
                var mapId = FindVoyageStart(row.RowId);
                var mapName = PointName(mapId);
                return new RouteDestination(
                    row.RowId,
                    RouteDisplayFormatter.ExtractPointCode(row.RowId, name),
                    name,
                    mapId,
                    string.IsNullOrWhiteSpace(mapName) || mapName == mapId.ToString()
                        ? $"Map {Array.IndexOf(this.reversedMapStartSectors.Reverse().ToArray(), mapId) + 1}"
                        : mapName,
                    checked((int)row.RankReq))
                {
                    MapPosition = new RouteMapPosition(row.X, row.Z),
                };
            })
            .OrderBy(destination => destination.MapId)
            .ThenBy(destination => destination.SectorId)
            .ToArray();
        this.lastRank = this.rankSheet.Last(row => row.Capacity != 0).RowId;
        this.calculatedRoutes = LoadCalculatedRoutes(pluginDirectory);
        this.routeIndex = RouteCandidateIndex.Build(this.calculatedRoutes, this.sectorById);
        UnlockRules = BuildUnlockRules();
        this.plannerDataWarnings = BuildPlannerDataWarnings();
        foreach (var warning in this.plannerDataWarnings)
            this.log.Warning(warning);
    }

    public IReadOnlyList<UnlockRule> UnlockRules { get; }

    public IReadOnlyList<RouteDestination> RouteDestinations { get; }

    public int MaximumRank => checked((int)this.lastRank);

    public IReadOnlyList<string> GetPlannerDataWarnings() => this.plannerDataWarnings;

    public SubmarineBuild ResolveBuild(string buildCode, int rank)
    {
        var parts = ParseBuildCode(buildCode);
        return ResolveBuild(parts, rank);
    }

    public SubmarineBuild? ResolveBuild(SubmarineBuildParts buildParts, int rank)
    {
        if (buildParts == SubmarineBuildParts.Empty ||
            buildParts.Hull == 0 ||
            buildParts.Stern == 0 ||
            buildParts.Bow == 0 ||
            buildParts.Bridge == 0)
        {
            return null;
        }

        try
        {
            return ResolveBuild(new PartIds(buildParts.Hull, buildParts.Stern, buildParts.Bow, buildParts.Bridge), rank);
        }
        catch
        {
            return null;
        }
    }

    private SubmarineBuild ResolveBuild(PartIds parts, int rank)
    {
        var rankRow = this.rankSheet.GetRow((uint)rank);
        var hull = this.partSheet.GetRow((uint)parts.Hull);
        var stern = this.partSheet.GetRow((uint)parts.Stern);
        var bow = this.partSheet.GetRow((uint)parts.Bow);
        var bridge = this.partSheet.GetRow((uint)parts.Bridge);

        return new SubmarineBuild(
            FormatBuildCode(parts),
            rank,
            rankRow.SurveillanceBonus + hull.Surveillance + stern.Surveillance + bow.Surveillance + bridge.Surveillance,
            rankRow.RetrievalBonus + hull.Retrieval + stern.Retrieval + bow.Retrieval + bridge.Retrieval,
            rankRow.FavorBonus + hull.Favor + stern.Favor + bow.Favor + bridge.Favor,
            rankRow.RangeBonus + hull.Range + stern.Range + bow.Range + bridge.Range,
            rankRow.SpeedBonus + hull.Speed + stern.Speed + bow.Speed + bridge.Speed);
    }

    public RouteSearchResult FindBestRoute(RouteSearchRequest request)
    {
        request.CancellationToken.ThrowIfCancellationRequested();
        this.routeQueries++;
        var cacheKey = RouteSearchCacheKey.Create(request);
        if (this.routeSearchCache.TryGetValue(cacheKey, out var cached))
        {
            this.routeCacheHits++;
            return new RouteSearchResult(cached.Route, 0, CacheHit: true);
        }

        var settings = request.Settings;
        var build = request.Build;
        var durationLimitHours = settings.GetEffectiveDurationLimitHours();
        var optimizeExpPerHour = settings.GetEffectiveOptimizeExpPerHour();
        SearchOutcome outcome;

        if (request.MustIncludeMask.IsEmpty)
        {
            var rankingKey = RouteRankingKey.Create(request);
            if (this.routeRankingCache.TryGetValue(rankingKey, out var rankedRouteIds))
            {
                this.rankingCacheHits++;
                outcome = FindBestRanked(
                    request,
                    rankedRouteIds,
                    durationLimitHours,
                    optimizeExpPerHour);
            }
            else
            {
                outcome = FindBestExhaustive(
                    request,
                    durationLimitHours,
                    optimizeExpPerHour,
                    rankingKey);
            }
        }
        else
        {
            outcome = FindBestExhaustive(
                request,
                durationLimitHours,
                optimizeExpPerHour,
                rankingKey: null);
        }

        if (outcome.Completed)
            this.exactCacheEvictions += this.routeSearchCache.Set(cacheKey, new CachedRoute(outcome.Route));

        return new RouteSearchResult(outcome.Route, outcome.Evaluated, CacheHit: false);
    }

    private SearchOutcome FindBestExhaustive(
        RouteSearchRequest request,
        int durationLimitHours,
        bool optimizeExpPerHour,
        RouteRankingKey? rankingKey)
    {
        var build = request.Build;
        var settings = request.Settings;
        var rankingValues = rankingKey is null ? null : new List<RouteRankValue>();
        var rankingStopwatch = rankingKey is null ? null : System.Diagnostics.Stopwatch.StartNew();
        var completed = true;
        var evaluated = 0;
        RouteRecord? bestRecord = null;
        TimeSpan bestDuration = TimeSpan.Zero;
        uint bestExp = 0;
        double bestScore = double.MinValue;

        foreach (var route in this.routeIndex.Enumerate(request.MustIncludeMask, build.Range))
        {
            if ((evaluated & 0x3FF) == 0)
            {
                request.CancellationToken.ThrowIfCancellationRequested();
                if (IsTimedOut(request.DeadlineUtc))
                {
                    completed = false;
                    break;
                }
            }

            evaluated++;
            if (route.MaxRequiredRank > build.Rank)
                continue;

            var exp = GetCachedExp(route, build, settings.GetEffectiveExpMode());
            if (exp == 0)
                continue;

            var duration = GetCachedDuration(route, build);
            if (duration == TimeSpan.Zero ||
                (durationLimitHours > 0 && duration > TimeSpan.FromHours(durationLimitHours)))
            {
                continue;
            }

            var score = optimizeExpPerHour
                ? exp / Math.Max(duration.TotalHours, 0.01)
                : exp;

            rankingValues?.Add(new RouteRankValue(route.Id, score, duration.Ticks));

            if (!request.UnlockedMask.ContainsAll(route.Mask) ||
                route.Mask.Intersects(request.ExcludedSectorMask))
            {
                continue;
            }

            var candidateValue = new RouteRankValue(route.Id, score, duration.Ticks);
            var bestValue = new RouteRankValue(bestRecord?.Id ?? -1, bestScore, bestDuration.Ticks);
            if (bestRecord is not null && !ExactRouteRanking.IsBetter(candidateValue, bestValue))
            {
                continue;
            }

            bestRecord = route;
            bestDuration = duration;
            bestExp = exp;
            bestScore = score;
        }

        this.routesEvaluated += evaluated;
        this.exhaustiveRoutesEvaluated += evaluated;

        if (completed && rankingKey is not null)
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            var rankedRouteIds = ExactRouteRanking.Create(rankingValues!);
            rankingStopwatch!.Stop();
            request.CancellationToken.ThrowIfCancellationRequested();
            if (IsTimedOut(request.DeadlineUtc))
            {
                completed = false;
            }
            else
            {
                this.rankingBuilds++;
                this.rankingBuildMilliseconds += rankingStopwatch.ElapsedMilliseconds;
                this.rankingCacheEvictions += this.routeRankingCache.Set(rankingKey.Value, rankedRouteIds);
            }
        }

        return new SearchOutcome(
            CreateRouteCandidate(bestRecord, bestExp, bestDuration, request, durationLimitHours),
            evaluated,
            completed);
    }

    private SearchOutcome FindBestRanked(
        RouteSearchRequest request,
        IReadOnlyList<int> rankedRouteIds,
        int durationLimitHours,
        bool optimizeExpPerHour)
    {
        RouteRankValue GetValue(int routeId)
        {
            var route = this.routeIndex.GetById(routeId);
            var exp = GetCachedExp(route, request.Build, request.Settings.GetEffectiveExpMode());
            var duration = GetCachedDuration(route, request.Build);
            var score = optimizeExpPerHour
                ? exp / Math.Max(duration.TotalHours, 0.01)
                : exp;
            return new RouteRankValue(routeId, score, duration.Ticks);
        }

        var selectedId = ExactRouteRanking.FindBest(
            rankedRouteIds,
            routeId =>
            {
                var route = this.routeIndex.GetById(routeId);
                return request.UnlockedMask.ContainsAll(route.Mask) &&
                       !route.Mask.Intersects(request.ExcludedSectorMask);
            },
            GetValue,
            () => IsTimedOut(request.DeadlineUtc),
            request.CancellationToken.ThrowIfCancellationRequested,
            out var evaluated,
            out var completed);

        this.routesEvaluated += evaluated;
        this.rankedRoutesEvaluated += evaluated;
        if (selectedId is null)
            return new SearchOutcome(null, evaluated, completed);

        var selected = this.routeIndex.GetById(selectedId.Value);
        var exp = GetCachedExp(selected, request.Build, request.Settings.GetEffectiveExpMode());
        var duration = GetCachedDuration(selected, request.Build);
        return new SearchOutcome(
            CreateRouteCandidate(selected, exp, duration, request, durationLimitHours),
            evaluated,
            completed);
    }

    private RouteCandidate? CreateRouteCandidate(
        RouteRecord? route,
        uint exp,
        TimeSpan duration,
        RouteSearchRequest request,
        int durationLimitHours)
        => route is null
            ? null
            : new RouteCandidate(
                route.Sectors,
                exp,
                duration,
                exp / Math.Max(duration.TotalHours, 0.01),
                GetUnlockTargets(route.Sectors, request.UnlockedPoints),
                request.Settings.EtaModel,
                durationLimitHours > 0);

    private TimeSpan GetCachedDuration(RouteRecord route, SubmarineBuild build)
    {
        if (!this.durationTicksBySpeed.TryGetValue(build.Speed, out var values))
        {
            values = new long[this.routeIndex.RouteCount];
            this.durationTicksBySpeed[build.Speed] = values;
        }

        var ticks = values[route.Id];
        if (ticks != 0)
            return TimeSpan.FromTicks(ticks);

        var calculated = CalculateDuration(route.Sectors, build);
        values[route.Id] = calculated.Ticks;
        return calculated;
    }

    private uint GetCachedExp(RouteRecord route, SubmarineBuild build, ExpMode expMode)
    {
        var profile = new ExpProfile(build.Surveillance, build.Retrieval, build.Favor, expMode);
        if (!this.sectorExpByProfile.TryGetValue(profile, out var values))
        {
            values = new uint[this.sectorById.Keys.Max() + 1];
            foreach (var (sectorId, sector) in this.sectorById)
            {
                var bonus = PredictBonusExp(sectorId, build);
                values[sectorId] = CalculateBonusExp(
                    expMode == ExpMode.Average ? bonus.Average : bonus.Guaranteed,
                    sector.ExpReward);
            }

            this.sectorExpByProfile[profile] = values;
        }

        var exp = 0u;
        foreach (var sectorId in route.Sectors)
            exp += values[sectorId];

        return exp;
    }

    private static bool IsTimedOut(DateTimeOffset? deadlineUtc)
        => deadlineUtc is not null && DateTimeOffset.UtcNow >= deadlineUtc.Value;

    public void ResetRouteSearchMetrics()
    {
        this.routeQueries = 0;
        this.routeCacheHits = 0;
        this.routesEvaluated = 0;
        this.rankingBuilds = 0;
        this.rankingCacheHits = 0;
        this.rankedRoutesEvaluated = 0;
        this.exhaustiveRoutesEvaluated = 0;
        this.rankingBuildMilliseconds = 0;
        this.exactCacheEvictions = 0;
        this.rankingCacheEvictions = 0;
    }

    public RouteSearchMetrics GetRouteSearchMetrics()
        => new(
            this.routeQueries,
            this.routeCacheHits,
            this.routesEvaluated,
            this.rankingBuilds,
            this.rankingCacheHits,
            this.rankedRoutesEvaluated,
            this.exhaustiveRoutesEvaluated,
            this.rankingBuildMilliseconds,
            this.exactCacheEvictions,
            this.rankingCacheEvictions);

    public uint CalculateExp(IReadOnlyList<uint> route, SubmarineBuild build, ExpMode expMode)
    {
        var expGain = 0u;
        foreach (var sectorId in route)
        {
            if (!this.sectorById.TryGetValue(sectorId, out var sector))
                continue;

            var bonus = PredictBonusExp(sectorId, build);
            expGain += CalculateBonusExp(expMode == ExpMode.Average ? bonus.Average : bonus.Guaranteed, sector.ExpReward);
        }

        return expGain;
    }

    public TimeSpan CalculateDuration(IReadOnlyList<uint> route, SubmarineBuild build)
    {
        if (route.Count is 0 or > 5)
            return TimeSpan.Zero;
        if (!this.sectorById.TryGetValue(route[0], out var first))
            return TimeSpan.Zero;
        if (!this.sectorById.TryGetValue(FindVoyageStart(route[0]), out var start))
            return TimeSpan.Zero;

        var seconds = CalcTime(start, first, build.Speed);
        for (var i = 1; i < route.Count; i++)
        {
            if (!this.sectorById.TryGetValue(route[i - 1], out var previous))
                return TimeSpan.Zero;
            if (!this.sectorById.TryGetValue(route[i], out var current))
                return TimeSpan.Zero;

            seconds += CalcTime(previous, current, build.Speed);
        }

        return TimeSpan.FromSeconds(seconds + FixedVoyageTimeSeconds);
    }

    public RouteFuelProfile CalculateFuel(IReadOnlyCollection<uint> sectors)
        => this.routeOperationalCalculator.CalculateFuel(sectors);

    public OrderedRouteOperationalProfile AnalyzeOrderedRoute(
        IReadOnlyList<uint> route,
        SubmarineBuild build)
        => this.routeOperationalCalculator.AnalyzeOrderedRoute(route, build);

    public (int Rank, uint CurrentExp, uint NextLevelExp) ApplyExp(int rank, uint currentExp, uint gainedExp, int targetRank)
    {
        var totalExp = currentExp + gainedExp;
        while (rank < targetRank)
        {
            var rankRow = this.rankSheet.GetRow((uint)rank);
            if (rankRow.ExpToNext > totalExp)
                break;

            totalExp -= rankRow.ExpToNext;
            rank++;

            if (rank > this.lastRank)
            {
                rank--;
                totalExp = 0;
                break;
            }
        }

        if (rank >= targetRank)
            totalExp = 0;

        return (rank, totalExp, rank >= targetRank ? 0 : this.rankSheet.GetRow((uint)rank).ExpToNext);
    }

    public string PointName(uint point)
    {
        if (!this.sectorById.TryGetValue(point, out var sector))
            return point.ToString();

        var name = sector.Destination.ExtractText();
        return string.IsNullOrWhiteSpace(name) ? point.ToString() : name;
    }

    public int GetPointRequiredRank(uint point)
        => this.sectorById.TryGetValue(point, out var sector) ? (int)sector.RankReq : int.MaxValue;

    public RouteSelectionValidation ValidateRoute(
        IReadOnlyList<uint> route,
        SubmarineBuild build,
        IReadOnlySet<uint> unlockedPoints)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(unlockedPoints);

        var selected = route.ToArray();
        var errors = new List<string>();
        if (selected.Length == 0)
            errors.Add("Select at least one destination.");
        if (selected.Length > 5)
            errors.Add("A voyage can visit at most five destinations.");
        if (selected.Distinct().Count() != selected.Length)
            errors.Add("A destination can only be selected once.");

        var known = selected.Where(this.sectorById.ContainsKey).Select(point => this.sectorById[point]).ToArray();
        var unknown = selected.Where(point => !this.sectorById.ContainsKey(point)).ToArray();
        if (unknown.Length > 0)
            errors.Add($"Unknown destinations: {string.Join(", ", unknown)}.");
        var locked = known.Where(sector => !unlockedPoints.Contains(sector.RowId)).ToArray();
        if (locked.Length > 0)
            errors.Add($"Not unlocked: {string.Join(", ", locked.Select(sector => PointName(sector.RowId)))}.");
        var aboveRank = known.Where(sector => sector.RankReq > build.Rank).ToArray();
        if (aboveRank.Length > 0)
            errors.Add($"Requires a higher rank: {string.Join(", ", aboveRank.Select(sector => PointName(sector.RowId)))}.");
        if (known.Select(sector => FindVoyageStart(sector.RowId)).Distinct().Skip(1).Any())
            errors.Add("Every destination must be on the same map.");

        var structurallyValid = selected.Length is >= 1 and <= 5 &&
                                known.Length == selected.Length &&
                                known.Select(sector => FindVoyageStart(sector.RowId)).Distinct().Count() <= 1 &&
                                selected.Distinct().Count() == selected.Length;
        var exactRoute = structurallyValid ? this.routeIndex.FindExact(selected) : null;
        if (structurallyValid && exactRoute is null)
            errors.Add("This ordered route is not available in the current voyage data.");
        else if (exactRoute is not null && exactRoute.Distance > build.Range)
            errors.Add($"This route needs {exactRoute.Distance:N0} range; the current build has {build.Range:N0}.");

        var fuel = structurallyValid ? CalculateFuel(selected) : null;
        TimeSpan? duration = structurallyValid ? CalculateDuration(selected, build) : null;
        return new RouteSelectionValidation(
            selected,
            errors.ToArray(),
            fuel is { IsComplete: true } ? fuel.CeruleumTanks : null,
            duration > TimeSpan.Zero ? duration : null);
    }

    private CalculatedRouteData LoadCalculatedRoutes(string pluginDirectory)
    {
        var path = Path.Combine(pluginDirectory, "CalculatedData.msgpack");
        try
        {
            using var stream = File.OpenRead(path);
            var data = MessagePackSerializer.Deserialize<CalculatedRouteData>(stream);
            this.log.Information(
                "Loaded {RouteCount} calculated submarine routes across {MapCount} maps.",
                data.Maps.Values.Sum(routes => routes.Length),
                data.Maps.Count);
            return data;
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Failed loading CalculatedData.msgpack");
            return new CalculatedRouteData();
        }
    }

    private IReadOnlyList<uint> GetUnlockTargets(IReadOnlyList<uint> route, IReadOnlySet<uint> unlockedPoints)
        => UnlockRules
            .Where(rule => route.Contains(rule.SourcePoint))
            .Where(rule => !unlockedPoints.Contains(rule.UnlocksPoint))
            .Select(rule => rule.UnlocksPoint)
            .Distinct()
            .ToArray();

    private IReadOnlyList<UnlockRule> BuildUnlockRules()
    {
        var rules = new List<UnlockRule>();
        foreach (var (target, source) in ExactUnlocks.SectorToUnlock)
        {
            if (source.Sector is SectorType.Begin or SectorType.Map or SectorType.UnknownUnlock)
                continue;
            if (!this.sectorById.TryGetValue(target, out var targetSector))
                continue;
            if (!this.sectorById.TryGetValue((uint)source.Sector, out var sourceSector))
                continue;

            rules.Add(new UnlockRule(
                (uint)source.Sector,
                target,
                (int)sourceSector.RankReq,
                (int)targetSector.RankReq,
                source.Sub,
                ExactUnlocks.MapProgressionPoints.Contains(target),
                ExactUnlocks.MainProgressionPoints.Contains(target)));
        }

        return rules;
    }

    private IReadOnlyList<string> BuildPlannerDataWarnings()
    {
        var warnings = new List<string>();
        if (this.calculatedRoutes.Maps.Count == 0 || this.routeIndex.RouteCount == 0)
        {
            warnings.Add("Bundled submarine route data could not be loaded. Route forecasts are incomplete.");
            return warnings;
        }

        var liveDestinations = this.sectorById.Values
            .Where(sector => !sector.StartingPoint && sector.ExpReward != 0)
            .Select(sector => sector.RowId)
            .ToHashSet();
        var cachedDestinations = this.calculatedRoutes.Maps.Values
            .SelectMany(routes => routes)
            .SelectMany(route => route.Sectors)
            .ToHashSet();
        var missingRouteDestinations = liveDestinations
            .Except(cachedDestinations)
            .Order()
            .ToArray();
        if (missingRouteDestinations.Length > 0)
        {
            warnings.Add(
                $"Bundled route data is older than the live game data and is missing " +
                $"{missingRouteDestinations.Length} destination(s): {FormatSectorIds(missingRouteDestinations)}. " +
                "Forecasts involving those destinations are incomplete until the route data is updated.");
        }

        var cachedMaximum = cachedDestinations.Count == 0 ? 0 : cachedDestinations.Max();
        var unlockMaximum = UnlockRules.Count == 0 ? 0 : UnlockRules.Max(rule => rule.UnlocksPoint);
        if (cachedMaximum > unlockMaximum)
        {
            warnings.Add(
                $"Bundled unlock rules end at sector {unlockMaximum}, but route data reaches sector {cachedMaximum}. " +
                "Forecasts involving newer unlocks are incomplete until the unlock catalog is updated.");
        }

        return warnings;
    }

    private static string FormatSectorIds(IReadOnlyList<uint> sectorIds)
    {
        const int displayLimit = 8;
        var displayed = string.Join(", ", sectorIds.Take(displayLimit));
        return sectorIds.Count <= displayLimit ? displayed : $"{displayed}, …";
    }

    private uint FindVoyageStart(uint sector)
        => this.reversedMapStartSectors.FirstOrDefault(start => sector >= start);

    private static uint CalcTime(SubmarineExploration from, SubmarineExploration to, float speed)
        => GetVoyageTime(from, to, speed) + GetSurveyTime(to, speed);

    private static uint GetSurveyTime(SubmarineExploration sector, float speed)
    {
        if (speed < 1)
            speed = 1;

        return (uint)Math.Floor(sector.SurveyDurationmin * 7000 / (speed * 100) * 60);
    }

    private static uint GetVoyageTime(SubmarineExploration from, SubmarineExploration to, float speed)
    {
        if (speed < 1)
            speed = 1;

        var distance = Vector3.Distance(new Vector3(from.X, from.Y, from.Z), new Vector3(to.X, to.Y, to.Z));
        return (uint)Math.Floor(distance * 3990 / (speed * 100) * 60);
    }

    private static (int Guaranteed, int Average, int Maximum) PredictBonusExp(uint sector, SubmarineBuild build)
    {
        if (!Breakpoints.MapBreakpoints.TryGetValue(sector, out var breakpoint))
            return (0, 0, 0);

        var guaranteed = breakpoint.Optimal <= build.Retrieval ? 1 : 0;

        var maximum = guaranteed;
        maximum += breakpoint.T2 <= build.Surveillance ? 1 : 0;
        maximum += breakpoint.T3 <= build.Surveillance ? 1 : 0;

        if (breakpoint.Favor <= build.Favor)
        {
            maximum += 1;
            maximum += breakpoint.T2 <= build.Surveillance ? 1 : 0;
            maximum += breakpoint.T3 <= build.Surveillance ? 1 : 0;
        }

        var max = Math.Clamp(maximum, 0, 4);
        var average = max == 0 ? 0 : (guaranteed + max) / 2;
        return (guaranteed, average, max);
    }

    private static uint CalculateBonusExp(int bonus, uint exp)
        => bonus switch
        {
            0 => exp,
            1 => (uint)(exp * 1.25),
            2 => (uint)(exp * 1.50),
            3 => (uint)(exp * 1.75),
            4 => (uint)(exp * 2.00),
            _ => exp,
        };

    private static PartIds ParseBuildCode(string buildCode)
    {
        var normalized = new string((buildCode ?? string.Empty).ToUpperInvariant().Where(char.IsLetter).Take(4).ToArray());
        normalized = (normalized + "SSSS")[..4];

        return new PartIds(
            ToPartId(normalized[0], 3),
            ToPartId(normalized[1], 4),
            ToPartId(normalized[2], 1),
            ToPartId(normalized[3], 2));
    }

    private static string FormatBuildCode(PartIds parts)
        => $"{ToIdentifier(parts.Hull)}{ToIdentifier(parts.Stern)}{ToIdentifier(parts.Bow)}{ToIdentifier(parts.Bridge)}";

    private static int ToPartId(char code, int offset)
        => code switch
        {
            'S' => 0 + offset,
            'U' => 4 + offset,
            'W' => 8 + offset,
            'C' => 12 + offset,
            'Y' => 16 + offset,
            _ => offset,
        };

    private static string ToIdentifier(int partId)
        => ((partId - 1) / 4) switch
        {
            0 => "S",
            1 => "U",
            2 => "W",
            3 => "C",
            4 => "Y",
            5 or 6 or 7 or 8 or 9 => $"{ToIdentifier(partId - 20)}+",
            _ => "?",
        };

    private readonly record struct PartIds(int Hull, int Stern, int Bow, int Bridge);

    [MessagePackObject]
    public sealed class CalculatedRouteData
    {
        [Key(0)]
        public uint MaxSector;

        [Key(1)]
        public Dictionary<int, CalculatedRoute[]> Maps = [];
    }

    [MessagePackObject]
    public struct CalculatedRoute
    {
        [Key(0)]
        public uint Distance;

        [Key(1)]
        public uint[] Sectors;
    }

    private enum SectorType : uint
    {
        UnknownUnlock = 9876,
        Begin = 9000,
        Map = 9999,
    }

    private sealed record UnlockSource(SectorType Sector, bool Sub = false)
    {
        public UnlockSource(uint sector, bool sub = false)
            : this((SectorType)sector, sub)
        {
        }
    }

    private static class ExactUnlocks
    {
        public static readonly HashSet<uint> MainProgressionPoints =
        [
            5, 10, 14, 15, 19, 20, 25, 26, 27, 28, 30,
            32, 33, 34, 37, 38, 39, 42, 43, 47, 49,
            53, 55, 57, 59, 62, 65, 70, 72,
            74, 75, 79, 83, 85, 89, 93,
            95, 96, 100, 104, 106, 111, 114,
            117, 121, 124, 128, 129, 133, 135,
        ];

        public static readonly HashSet<uint> MapProgressionPoints = [30, 49, 72, 93, 114, 135];

        public static readonly Dictionary<uint, UnlockSource> SectorToUnlock = new()
        {
            { 0, new UnlockSource(SectorType.Map) },
            { 1, new UnlockSource(SectorType.Begin) },
            { 2, new UnlockSource(SectorType.Begin) },
            { 3, new UnlockSource(1) },
            { 4, new UnlockSource(2) },
            { 5, new UnlockSource(2) },
            { 6, new UnlockSource(3) },
            { 7, new UnlockSource(4) },
            { 8, new UnlockSource(7) },
            { 9, new UnlockSource(5) },
            { 10, new UnlockSource(5, sub: true) },
            { 11, new UnlockSource(9) },
            { 12, new UnlockSource(8) },
            { 13, new UnlockSource(8) },
            { 14, new UnlockSource(10) },
            { 15, new UnlockSource(14, sub: true) },
            { 16, new UnlockSource(11) },
            { 17, new UnlockSource(16) },
            { 18, new UnlockSource(12) },
            { 19, new UnlockSource(15) },
            { 20, new UnlockSource(19, sub: true) },
            { 21, new UnlockSource(19) },
            { 22, new UnlockSource(21) },
            { 23, new UnlockSource(14) },
            { 24, new UnlockSource(23) },
            { 25, new UnlockSource(20) },
            { 26, new UnlockSource(25) },
            { 27, new UnlockSource(26) },
            { 28, new UnlockSource(27) },
            { 29, new UnlockSource(27) },
            { 30, new UnlockSource(28) },
            { 31, new UnlockSource(SectorType.Map) },
            { 32, new UnlockSource(30) },
            { 33, new UnlockSource(32) },
            { 34, new UnlockSource(33) },
            { 35, new UnlockSource(34) },
            { 36, new UnlockSource(35) },
            { 37, new UnlockSource(34) },
            { 38, new UnlockSource(37) },
            { 39, new UnlockSource(38) },
            { 40, new UnlockSource(38) },
            { 41, new UnlockSource(40) },
            { 42, new UnlockSource(39) },
            { 43, new UnlockSource(42) },
            { 44, new UnlockSource(40) },
            { 45, new UnlockSource(41) },
            { 46, new UnlockSource(45) },
            { 47, new UnlockSource(43) },
            { 48, new UnlockSource(36) },
            { 49, new UnlockSource(47) },
            { 50, new UnlockSource(45) },
            { 51, new UnlockSource(42) },
            { 52, new UnlockSource(SectorType.Map) },
            { 53, new UnlockSource(49) },
            { 54, new UnlockSource(53) },
            { 55, new UnlockSource(53) },
            { 56, new UnlockSource(55) },
            { 57, new UnlockSource(55) },
            { 58, new UnlockSource(56) },
            { 59, new UnlockSource(57) },
            { 60, new UnlockSource(57) },
            { 61, new UnlockSource(59) },
            { 62, new UnlockSource(59) },
            { 63, new UnlockSource(61) },
            { 64, new UnlockSource(61) },
            { 65, new UnlockSource(62) },
            { 66, new UnlockSource(65) },
            { 67, new UnlockSource(64) },
            { 68, new UnlockSource(66) },
            { 69, new UnlockSource(64) },
            { 70, new UnlockSource(65) },
            { 71, new UnlockSource(69) },
            { 72, new UnlockSource(70) },
            { 73, new UnlockSource(SectorType.Map) },
            { 74, new UnlockSource(72) },
            { 75, new UnlockSource(74) },
            { 76, new UnlockSource(74) },
            { 77, new UnlockSource(76) },
            { 78, new UnlockSource(75) },
            { 79, new UnlockSource(75) },
            { 80, new UnlockSource(76) },
            { 81, new UnlockSource(77) },
            { 82, new UnlockSource(81) },
            { 83, new UnlockSource(79) },
            { 84, new UnlockSource(83) },
            { 85, new UnlockSource(83) },
            { 86, new UnlockSource(81) },
            { 87, new UnlockSource(82) },
            { 88, new UnlockSource(84) },
            { 89, new UnlockSource(85) },
            { 90, new UnlockSource(87) },
            { 91, new UnlockSource(88) },
            { 92, new UnlockSource(88) },
            { 93, new UnlockSource(89) },
            { 94, new UnlockSource(SectorType.Map) },
            { 95, new UnlockSource(93) },
            { 96, new UnlockSource(95) },
            { 97, new UnlockSource(95) },
            { 98, new UnlockSource(96) },
            { 99, new UnlockSource(97) },
            { 100, new UnlockSource(96) },
            { 101, new UnlockSource(97) },
            { 102, new UnlockSource(101) },
            { 103, new UnlockSource(98) },
            { 104, new UnlockSource(100) },
            { 105, new UnlockSource(101) },
            { 106, new UnlockSource(104) },
            { 107, new UnlockSource(105) },
            { 108, new UnlockSource(106) },
            { 109, new UnlockSource(107) },
            { 110, new UnlockSource(103) },
            { 111, new UnlockSource(106) },
            { 112, new UnlockSource(109) },
            { 113, new UnlockSource(108) },
            { 114, new UnlockSource(111) },
            { 115, new UnlockSource(SectorType.Map) },
            { 116, new UnlockSource(114) },
            { 117, new UnlockSource(116) },
            { 118, new UnlockSource(116) },
            { 119, new UnlockSource(117) },
            { 120, new UnlockSource(118) },
            { 121, new UnlockSource(117) },
            { 122, new UnlockSource(118) },
            { 123, new UnlockSource(122) },
            { 124, new UnlockSource(121) },
            { 125, new UnlockSource(122) },
            { 126, new UnlockSource(123) },
            { 127, new UnlockSource(124) },
            { 128, new UnlockSource(124) },
            { 129, new UnlockSource(128) },
            { 130, new UnlockSource(127) },
            { 131, new UnlockSource(129) },
            { 132, new UnlockSource(127) },
            { 133, new UnlockSource(129) },
            { 134, new UnlockSource(132) },
            { 135, new UnlockSource(133) },
            { 136, new UnlockSource(SectorType.Map) },
            { 137, new UnlockSource(135) },
            { 138, new UnlockSource(137) },
            { 139, new UnlockSource(137) },
            { 140, new UnlockSource(139) },
            { 141, new UnlockSource(138) },
            { 142, new UnlockSource(140) },
            { 143, new UnlockSource(142) },
            { 144, new UnlockSource(143) },
            { 145, new UnlockSource(141) },
            { 146, new UnlockSource(145) },
            { 147, new UnlockSource(144) },
            { 148, new UnlockSource(146) },
            { 149, new UnlockSource(144) },
        };
    }

    private sealed record Breakpoint(int T2, int T3, int Normal, int Optimal, int Favor);

    private sealed record RouteRecord(
        int Id,
        int Map,
        uint Distance,
        uint[] Sectors,
        int MaxRequiredRank,
        SectorMask Mask);

    private sealed class RouteCandidateIndex
    {
        private readonly RouteRecord[] routesById;
        private readonly Dictionary<int, RouteRecord[]> routesByMap;
        private readonly Dictionary<uint, RouteRecord[]> routesBySector;
        private readonly Dictionary<string, RouteRecord> routesByOrderedSectors;

        private RouteCandidateIndex(RouteRecord[] routes)
        {
            var stableRoutes = routes
                .GroupBy(route => route.Map)
                .SelectMany(group => group.OrderBy(route => route.Distance))
                .Select((route, stableId) => route with { Id = stableId })
                .ToArray();
            this.routesById = stableRoutes;
            RouteCount = stableRoutes.Length;
            this.routesByMap = stableRoutes
                .GroupBy(route => route.Map)
                .ToDictionary(group => group.Key, group => group.ToArray());
            this.routesBySector = stableRoutes
                .SelectMany(route => route.Sectors.Select(sector => (sector, route)))
                .GroupBy(pair => pair.sector)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(pair => pair.route).OrderBy(route => route.Distance).ToArray());
            this.routesByOrderedSectors = stableRoutes
                .GroupBy(route => RouteKey(route.Sectors), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        }

        public int RouteCount { get; }

        public RouteRecord GetById(int routeId) => this.routesById[routeId];

        public RouteRecord? FindExact(IReadOnlyList<uint> route)
            => this.routesByOrderedSectors.GetValueOrDefault(RouteKey(route));

        private static string RouteKey(IEnumerable<uint> route) => string.Join(',', route);

        public IEnumerable<RouteRecord> Enumerate(SectorMask mustInclude, int range)
        {
            if (!mustInclude.IsEmpty)
            {
                RouteRecord[]? smallestBucket = null;
                for (uint point = 0; point < 192; point++)
                {
                    if (!mustInclude.Contains(point))
                        continue;
                    if (!this.routesBySector.TryGetValue(point, out var routes))
                        yield break;
                    if (smallestBucket is null || routes.Length < smallestBucket.Length)
                        smallestBucket = routes;
                }

                if (smallestBucket is null)
                    yield break;

                var limit = UpperBound(smallestBucket, range);
                for (var i = 0; i < limit; i++)
                {
                    var route = smallestBucket[i];
                    if (route.Mask.ContainsAll(mustInclude))
                        yield return route;
                }

                yield break;
            }

            foreach (var routes in this.routesByMap.Values)
            {
                var limit = UpperBound(routes, range);
                for (var i = 0; i < limit; i++)
                    yield return routes[i];
            }
        }

        public static RouteCandidateIndex Build(
            CalculatedRouteData data,
            IReadOnlyDictionary<uint, SubmarineExploration> sectorById)
        {
            var records = new List<RouteRecord>();
            var id = 0;
            foreach (var (map, routes) in data.Maps)
            {
                foreach (var route in routes)
                {
                    if (route.Sectors.Length is 0 or > 5)
                        continue;

                    var maxRank = 0;
                    var valid = true;
                    foreach (var sectorId in route.Sectors)
                    {
                        if (!sectorById.TryGetValue(sectorId, out var sector) || sector.StartingPoint || sector.ExpReward == 0)
                        {
                            valid = false;
                            break;
                        }

                        maxRank = Math.Max(maxRank, (int)sector.RankReq);
                    }

                    if (valid)
                    {
                        records.Add(new RouteRecord(
                            id++,
                            map,
                            route.Distance,
                            route.Sectors,
                            maxRank,
                            SectorMask.From(route.Sectors)));
                    }
                }
            }

            return new RouteCandidateIndex(records.ToArray());
        }

        private static int UpperBound(RouteRecord[] routes, int range)
        {
            var low = 0;
            var high = routes.Length;
            while (low < high)
            {
                var middle = low + ((high - low) / 2);
                if (routes[middle].Distance <= (uint)Math.Max(0, range))
                    low = middle + 1;
                else
                    high = middle;
            }

            return low;
        }
    }

    private readonly record struct ExpProfile(
        int Surveillance,
        int Retrieval,
        int Favor,
        ExpMode ExpMode);

    private readonly record struct SearchOutcome(RouteCandidate? Route, int Evaluated, bool Completed);

    private readonly record struct CachedRoute(RouteCandidate? Route);

    private readonly record struct RouteRankingKey(
        int Rank,
        int Surveillance,
        int Retrieval,
        int Favor,
        int Range,
        int Speed,
        ExpMode ExpMode,
        int DurationLimitHours,
        bool OptimizeExpPerHour)
    {
        public static RouteRankingKey Create(RouteSearchRequest request)
            => new(
                request.Build.Rank,
                request.Build.Surveillance,
                request.Build.Retrieval,
                request.Build.Favor,
                request.Build.Range,
                request.Build.Speed,
                request.Settings.GetEffectiveExpMode(),
                request.Settings.GetEffectiveDurationLimitHours(),
                request.Settings.GetEffectiveOptimizeExpPerHour());
    }

    private readonly record struct RouteSearchCacheKey(
        int Rank,
        int Surveillance,
        int Retrieval,
        int Favor,
        int Range,
        int Speed,
        EtaModel EtaModel,
        ExpMode ExpMode,
        int DurationLimitHours,
        bool OptimizeExpPerHour,
        SectorMask UnlockedPoints,
        SectorMask MustIncludePoints,
        SectorMask ExcludedSectorPoints)
    {
        public static RouteSearchCacheKey Create(RouteSearchRequest request)
            => new(
                request.Build.Rank,
                request.Build.Surveillance,
                request.Build.Retrieval,
                request.Build.Favor,
                request.Build.Range,
                request.Build.Speed,
                request.Settings.EtaModel,
                request.Settings.GetEffectiveExpMode(),
                request.Settings.GetEffectiveDurationLimitHours(),
                request.Settings.GetEffectiveOptimizeExpPerHour(),
                request.UnlockedMask,
                request.MustIncludeMask,
                request.ExcludedSectorMask);
    }

    private static class Breakpoints
    {
        public static readonly Dictionary<uint, Breakpoint> MapBreakpoints = new()
        {
            { 001, new Breakpoint(020, 080, 050, 080, 070) }, { 002, new Breakpoint(020, 080, 050, 080, 070) },
            { 003, new Breakpoint(020, 085, 055, 085, 070) }, { 004, new Breakpoint(020, 085, 055, 085, 070) },
            { 005, new Breakpoint(025, 090, 060, 090, 080) }, { 006, new Breakpoint(025, 090, 060, 090, 080) },
            { 007, new Breakpoint(030, 095, 065, 095, 090) }, { 008, new Breakpoint(030, 100, 070, 100, 090) },
            { 009, new Breakpoint(035, 110, 075, 105, 090) }, { 010, new Breakpoint(050, 115, 080, 110, 090) },
            { 011, new Breakpoint(050, 090, 080, 110, 070) }, { 012, new Breakpoint(055, 095, 090, 120, 080) },
            { 013, new Breakpoint(060, 100, 100, 130, 075) }, { 014, new Breakpoint(060, 100, 100, 130, 085) },
            { 015, new Breakpoint(080, 115, 120, 160, 090) }, { 016, new Breakpoint(060, 100, 100, 130, 085) },
            { 017, new Breakpoint(065, 105, 110, 140, 090) }, { 018, new Breakpoint(085, 120, 135, 175, 095) },
            { 019, new Breakpoint(075, 110, 120, 155, 095) }, { 020, new Breakpoint(090, 125, 140, 180, 100) },
            { 021, new Breakpoint(090, 120, 135, 175, 095) }, { 022, new Breakpoint(105, 130, 140, 180, 100) },
            { 023, new Breakpoint(110, 140, 140, 180, 105) }, { 024, new Breakpoint(120, 130, 145, 190, 105) },
            { 025, new Breakpoint(120, 135, 145, 190, 105) }, { 026, new Breakpoint(135, 140, 150, 195, 110) },
            { 027, new Breakpoint(130, 145, 150, 195, 110) }, { 028, new Breakpoint(130, 150, 155, 200, 120) },
            { 029, new Breakpoint(135, 150, 160, 200, 130) }, { 030, new Breakpoint(140, 155, 170, 215, 135) },
            { 032, new Breakpoint(135, 150, 165, 205, 140) }, { 033, new Breakpoint(140, 155, 170, 205, 140) },
            { 034, new Breakpoint(140, 160, 175, 210, 145) }, { 035, new Breakpoint(145, 165, 180, 220, 145) },
            { 036, new Breakpoint(145, 160, 185, 220, 150) }, { 037, new Breakpoint(145, 165, 180, 220, 145) },
            { 038, new Breakpoint(150, 170, 180, 220, 140) }, { 039, new Breakpoint(160, 175, 190, 225, 150) },
            { 040, new Breakpoint(155, 170, 190, 220, 140) }, { 041, new Breakpoint(160, 175, 190, 225, 150) },
            { 042, new Breakpoint(155, 170, 185, 230, 160) }, { 043, new Breakpoint(160, 175, 185, 235, 165) },
            { 044, new Breakpoint(160, 170, 190, 240, 175) }, { 045, new Breakpoint(165, 190, 195, 245, 170) },
            { 046, new Breakpoint(170, 185, 205, 250, 175) }, { 047, new Breakpoint(165, 180, 185, 235, 165) },
            { 048, new Breakpoint(165, 180, 185, 235, 165) }, { 049, new Breakpoint(170, 185, 190, 240, 165) },
            { 050, new Breakpoint(175, 190, 200, 250, 175) }, { 051, new Breakpoint(180, 190, 200, 250, 175) },
            { 053, new Breakpoint(180, 190, 200, 250, 175) }, { 054, new Breakpoint(180, 190, 200, 250, 175) },
            { 055, new Breakpoint(180, 190, 200, 250, 175) }, { 056, new Breakpoint(180, 195, 205, 260, 178) },
            { 057, new Breakpoint(180, 195, 210, 260, 185) }, { 058, new Breakpoint(180, 195, 210, 265, 185) },
            { 059, new Breakpoint(180, 195, 215, 270, 185) }, { 060, new Breakpoint(180, 195, 220, 270, 185) },
            { 061, new Breakpoint(180, 195, 220, 270, 185) }, { 062, new Breakpoint(180, 195, 220, 270, 185) },
            { 063, new Breakpoint(185, 200, 225, 275, 190) }, { 064, new Breakpoint(185, 200, 230, 280, 190) },
            { 065, new Breakpoint(185, 200, 230, 280, 190) }, { 066, new Breakpoint(190, 205, 235, 285, 195) },
            { 067, new Breakpoint(195, 210, 240, 290, 200) }, { 068, new Breakpoint(195, 210, 245, 295, 200) },
            { 069, new Breakpoint(200, 215, 255, 300, 205) }, { 070, new Breakpoint(205, 220, 255, 300, 210) },
            { 071, new Breakpoint(205, 220, 260, 305, 210) }, { 072, new Breakpoint(205, 220, 260, 305, 210) },
            { 074, new Breakpoint(205, 220, 260, 305, 210) }, { 075, new Breakpoint(205, 220, 260, 305, 210) },
            { 076, new Breakpoint(205, 220, 260, 305, 210) }, { 077, new Breakpoint(210, 225, 265, 310, 215) },
            { 078, new Breakpoint(210, 225, 265, 310, 215) }, { 079, new Breakpoint(210, 225, 265, 310, 215) },
            { 080, new Breakpoint(210, 225, 265, 310, 215) }, { 081, new Breakpoint(215, 230, 270, 315, 220) },
            { 082, new Breakpoint(215, 230, 270, 315, 220) }, { 083, new Breakpoint(215, 230, 270, 315, 220) },
            { 084, new Breakpoint(215, 230, 270, 315, 220) }, { 085, new Breakpoint(215, 230, 270, 315, 220) },
            { 086, new Breakpoint(215, 230, 270, 315, 220) }, { 087, new Breakpoint(220, 235, 275, 320, 225) },
            { 088, new Breakpoint(220, 235, 275, 320, 225) }, { 089, new Breakpoint(220, 235, 275, 320, 225) },
            { 090, new Breakpoint(220, 235, 275, 320, 225) }, { 091, new Breakpoint(220, 235, 275, 320, 225) },
            { 092, new Breakpoint(220, 235, 275, 320, 225) }, { 093, new Breakpoint(220, 235, 275, 320, 225) },
            { 095, new Breakpoint(220, 235, 275, 320, 225) }, { 096, new Breakpoint(220, 235, 275, 320, 225) },
            { 097, new Breakpoint(220, 235, 275, 320, 225) }, { 098, new Breakpoint(225, 240, 280, 325, 230) },
            { 099, new Breakpoint(225, 237, 280, 325, 227) }, { 100, new Breakpoint(225, 238, 280, 325, 230) },
            { 101, new Breakpoint(225, 240, 280, 325, 230) }, { 102, new Breakpoint(226, 241, 281, 326, 231) },
            { 103, new Breakpoint(227, 242, 282, 327, 232) }, { 104, new Breakpoint(228, 243, 283, 328, 233) },
            { 105, new Breakpoint(229, 244, 284, 329, 234) }, { 106, new Breakpoint(230, 245, 285, 330, 235) },
            { 107, new Breakpoint(230, 245, 285, 330, 235) }, { 108, new Breakpoint(231, 246, 286, 331, 236) },
            { 109, new Breakpoint(232, 247, 287, 332, 237) }, { 110, new Breakpoint(233, 248, 288, 333, 238) },
            { 111, new Breakpoint(234, 249, 289, 334, 239) }, { 112, new Breakpoint(234, 249, 289, 334, 239) },
            { 113, new Breakpoint(235, 250, 290, 335, 240) }, { 114, new Breakpoint(235, 250, 290, 335, 240) },
            { 116, new Breakpoint(235, 250, 290, 335, 240) }, { 117, new Breakpoint(235, 250, 290, 335, 240) },
            { 118, new Breakpoint(235, 250, 290, 335, 240) }, { 119, new Breakpoint(236, 251, 291, 336, 241) },
            { 120, new Breakpoint(237, 252, 292, 337, 242) }, { 121, new Breakpoint(238, 253, 293, 338, 243) },
            { 122, new Breakpoint(240, 255, 295, 340, 245) }, { 123, new Breakpoint(241, 256, 296, 341, 246) },
            { 124, new Breakpoint(242, 257, 297, 342, 247) }, { 125, new Breakpoint(243, 258, 298, 343, 248) },
            { 126, new Breakpoint(244, 259, 299, 344, 249) }, { 127, new Breakpoint(245, 260, 300, 345, 250) },
            { 128, new Breakpoint(245, 260, 300, 345, 250) }, { 129, new Breakpoint(246, 261, 301, 346, 251) },
            { 130, new Breakpoint(247, 262, 302, 347, 252) }, { 131, new Breakpoint(248, 263, 303, 348, 253) },
            { 132, new Breakpoint(249, 264, 304, 349, 254) }, { 133, new Breakpoint(249, 264, 304, 349, 254) },
            { 134, new Breakpoint(250, 265, 305, 350, 255) }, { 135, new Breakpoint(250, 266, 305, 350, 255) },
            { 137, new Breakpoint(251, 266, 306, 351, 256) }, { 138, new Breakpoint(252, 267, 307, 352, 257) },
            { 139, new Breakpoint(253, 268, 308, 353, 258) }, { 140, new Breakpoint(254, 269, 309, 354, 259) },
            { 141, new Breakpoint(254, 269, 309, 354, 259) }, { 142, new Breakpoint(255, 270, 310, 355, 260) },
            { 143, new Breakpoint(255, 270, 310, 355, 260) }, { 144, new Breakpoint(256, 271, 311, 356, 261) },
            { 145, new Breakpoint(257, 272, 312, 357, 262) }, { 146, new Breakpoint(258, 273, 313, 358, 263) },
            { 147, new Breakpoint(258, 273, 313, 358, 263) }, { 148, new Breakpoint(259, 274, 314, 359, 264) },
            { 149, new Breakpoint(260, 275, 315, 360, 265) },
        };
    }
}
