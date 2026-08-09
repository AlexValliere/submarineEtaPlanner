using SubmarineEtaPlanner.Planner;

namespace SubmarineEtaPlanner.SubmarineTrackerCompat;

public sealed class CompatSubmarineCatalog : ISubmarineCatalog, IRouteSelectionCatalog
{
    public int MaximumRank => 149;

    private static readonly Dictionary<char, PartStats> PartCatalog = new()
    {
        ['S'] = new PartStats(85, 85, 70, 85, 80),
        ['U'] = new PartStats(115, 95, 75, 115, 95),
        ['W'] = new PartStats(125, 105, 80, 130, 100),
        ['C'] = new PartStats(105, 125, 90, 120, 105),
    };

    private static readonly Dictionary<uint, SectorInfo> Sectors = Enumerable.Range(1, 149)
        .Select(i => CreateSector((uint)i))
        .ToDictionary(s => s.Point, s => s);

    public IReadOnlyList<RouteDestination> RouteDestinations { get; } = Sectors.Values
        .Select(sector => new RouteDestination(
            sector.Point,
            RouteDisplayFormatter.ExtractPointCode(sector.Point, $"{sector.MapCode}{sector.Point}"),
            $"{sector.MapCode}{sector.Point}",
            sector.MapId,
            sector.MapCode,
            sector.RequiredRank))
        .OrderBy(destination => destination.MapId)
        .ThenBy(destination => destination.SectorId)
        .ToArray();

    public IReadOnlyList<UnlockRule> UnlockRules { get; } =
    [
        new UnlockRule(5, 6, 10),
        new UnlockRule(10, 11, 15),
        new UnlockRule(15, 16, 20, UnlocksSubSlot: true),
        new UnlockRule(20, 21, 25),
        new UnlockRule(26, 27, 30),
        new UnlockRule(30, 32, 35),
        new UnlockRule(35, 36, 40, UnlocksSubSlot: true),
        new UnlockRule(44, 45, 50),
        new UnlockRule(48, 49, 55),
        new UnlockRule(60, 61, 60),
        new UnlockRule(70, 71, 70),
        new UnlockRule(80, 81, 80),
        new UnlockRule(90, 91, 90),
        new UnlockRule(100, 101, 100),
        new UnlockRule(110, 111, 110),
    ];

    public SubmarineBuild ResolveBuild(string buildCode, int rank)
    {
        var normalized = NormalizeBuildCode(buildCode);
        var stats = normalized.Select(c => PartCatalog.TryGetValue(c, out var part) ? part : PartCatalog['S']).ToArray();
        return new SubmarineBuild(
            normalized,
            rank,
            stats.Sum(s => s.Surveillance) + rank,
            stats.Sum(s => s.Retrieval) + rank,
            stats.Sum(s => s.Favor),
            stats.Sum(s => s.Range) + rank * 3,
            Math.Max(40, stats.Sum(s => s.Speed) / stats.Length));
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

        var buildCode = string.Concat(
            ToIdentifier(buildParts.Hull),
            ToIdentifier(buildParts.Stern),
            ToIdentifier(buildParts.Bow),
            ToIdentifier(buildParts.Bridge));
        return ResolveBuild(buildCode, rank);
    }

    public RouteSearchResult FindBestRoute(RouteSearchRequest request)
    {
        var candidates = GetCandidateRoutes(
            request.Build,
            request.UnlockedPoints,
            request.MustIncludeMask,
            request.ExcludedSectorMask,
            request.Settings,
            request.DeadlineUtc,
            request.CancellationToken);
        return new RouteSearchResult(candidates.FirstOrDefault(), candidates.Count, CacheHit: false);
    }

    private IReadOnlyList<RouteCandidate> GetCandidateRoutes(
        SubmarineBuild build,
        IReadOnlySet<uint> unlockedPoints,
        SectorMask mustIncludeMask,
        SectorMask excludedSectorMask,
        EtaSettings settings,
        DateTimeOffset? deadlineUtc,
        CancellationToken cancellationToken)
    {
        var mustInclude = Sectors.Keys.Where(mustIncludeMask.Contains).ToHashSet();
        var available = Sectors.Values
            .Where(s => s.RequiredRank <= build.Rank)
            .Where(s => unlockedPoints.Contains(s.Point))
            .OrderByDescending(s => mustInclude.Contains(s.Point))
            .ThenByDescending(s => s.Exp)
            .Take(24)
            .ToArray();

        if (mustInclude.Count > 0)
        {
            var must = Sectors.Values
                .Where(s => mustInclude.Contains(s.Point))
                .Where(s => s.RequiredRank <= build.Rank)
                .ToArray();
            available = must.Concat(available).DistinctBy(s => s.Point).ToArray();
        }

        var candidates = new List<RouteCandidate>();
        foreach (var route in BuildRouteSets(available.Select(s => s.Point).ToArray(), mustInclude))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (SectorMask.From(route).Intersects(excludedSectorMask))
                continue;
            var distance = route.Sum(p => Sectors[p].Distance);
            if (distance > build.Range)
                continue;

            var duration = CalculateDuration(route, build);
            var durationLimitHours = settings.GetEffectiveDurationLimitHours();
            if (durationLimitHours > 0 && duration > TimeSpan.FromHours(durationLimitHours))
                continue;

            var exp = CalculateExp(route, build, settings.GetEffectiveExpMode());
            var expPerHour = duration.TotalHours <= 0 ? exp : exp / duration.TotalHours;
            var unlockTargets = UnlockRules
                .Where(r => route.Contains(r.SourcePoint))
                .Where(r => r.SourceRequiredRank <= build.Rank)
                .Select(r => r.UnlocksPoint)
                .ToArray();

            candidates.Add(new RouteCandidate(
                route,
                exp,
                duration,
                expPerHour,
                unlockTargets,
                settings.EtaModel,
                durationLimitHours > 0));
        }

        return candidates
            .OrderByDescending(c => mustInclude.Count > 0 && c.Route.Any(mustInclude.Contains))
            .ThenByDescending(c => settings.GetEffectiveOptimizeExpPerHour() ? c.ExpPerHour : c.Exp)
            .ThenBy(c => c.Duration)
            .ToArray();
    }

    public uint CalculateExp(IReadOnlyList<uint> route, SubmarineBuild build, ExpMode expMode)
    {
        var multiplier = expMode == ExpMode.Average ? 1.2 : 1.0;
        var statBonus = build.Retrieval >= 420 ? 1.25 : build.Retrieval >= 360 ? 1.1 : 1.0;
        return (uint)route
            .Where(Sectors.ContainsKey)
            .Sum(point => Math.Round(Sectors[point].Exp * multiplier * statBonus));
    }

    public TimeSpan CalculateDuration(IReadOnlyList<uint> route, SubmarineBuild build)
    {
        if (route.Count == 0)
            return TimeSpan.Zero;

        var distance = route.Where(Sectors.ContainsKey).Sum(point => Sectors[point].Distance);
        var speed = Math.Max(1, build.Speed);
        var travelHours = Math.Max(1, distance / (double)speed);
        return TimeSpan.FromHours(12 + travelHours);
    }

    public (int Rank, uint CurrentExp, uint NextLevelExp) ApplyExp(int rank, uint currentExp, uint gainedExp, int targetRank)
    {
        var total = currentExp + gainedExp;
        while (rank < targetRank)
        {
            var needed = ExpToNext(rank);
            if (total < needed)
                break;

            total -= needed;
            rank++;
        }

        if (rank >= targetRank)
            total = 0;

        return (rank, total, rank >= targetRank ? 0 : ExpToNext(rank));
    }

    public string PointName(uint point) => Sectors.TryGetValue(point, out var sector) ? $"{sector.MapCode}{sector.Point}" : point.ToString();

    public int GetPointRequiredRank(uint point)
        => Sectors.TryGetValue(point, out var sector) ? sector.RequiredRank : int.MaxValue;

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

        var known = selected.Where(Sectors.ContainsKey).Select(point => Sectors[point]).ToArray();
        var unknown = selected.Where(point => !Sectors.ContainsKey(point)).ToArray();
        if (unknown.Length > 0)
            errors.Add($"Unknown destinations: {string.Join(", ", unknown)}.");
        var locked = known.Where(sector => !unlockedPoints.Contains(sector.Point)).ToArray();
        if (locked.Length > 0)
            errors.Add($"Not unlocked: {string.Join(", ", locked.Select(sector => sector.MapCode + sector.Point))}.");
        var aboveRank = known.Where(sector => sector.RequiredRank > build.Rank).ToArray();
        if (aboveRank.Length > 0)
            errors.Add($"Requires a higher rank: {string.Join(", ", aboveRank.Select(sector => sector.MapCode + sector.Point))}.");
        if (known.Select(sector => sector.MapId).Distinct().Skip(1).Any())
            errors.Add("Every destination must be on the same map.");

        var distance = known.Sum(sector => checked((long)sector.Distance));
        if (known.Length == selected.Length && distance > build.Range)
            errors.Add($"This route needs {distance:N0} range; the current build has {build.Range:N0}.");

        TimeSpan? duration = errors.Count == 0 ? CalculateDuration(selected, build) : null;
        return new RouteSelectionValidation(selected, errors.ToArray(), null, duration);
    }

    private static uint ExpToNext(int rank)
        => (uint)(1000 + rank * rank * 24 + rank * 240);

    private static IEnumerable<IReadOnlyList<uint>> BuildRouteSets(uint[] points, IReadOnlySet<uint> mustInclude)
    {
        if (points.Length == 0)
            yield break;

        foreach (var point in points)
            yield return [point];

        for (var size = 2; size <= Math.Min(5, points.Length); size++)
        {
            for (var i = 0; i <= points.Length - size; i++)
            {
                var route = points.Skip(i).Take(size).Distinct().ToArray();
                if (route.Length != size)
                    continue;
                if (mustInclude.Count > 0 && !route.Any(mustInclude.Contains))
                    continue;
                yield return route;
            }
        }
    }

    private static string NormalizeBuildCode(string buildCode)
    {
        var normalized = new string((buildCode ?? string.Empty).ToUpperInvariant().Where(char.IsLetter).Take(4).ToArray());
        if (normalized.Length == 4)
            return normalized;

        return (normalized + "SSSS")[..4];
    }

    private static char ToIdentifier(ushort partId)
        => ((partId - 1) / 4) switch
        {
            0 => 'S',
            1 => 'U',
            2 => 'W',
            3 => 'C',
            _ => 'S',
        };

    private static SectorInfo CreateSector(uint point)
    {
        var mapIndex = (point - 1) / 30;
        var requiredRank = Math.Max(1, (int)point - 2);
        var exp = (uint)(850 + point * 95 + Math.Pow(point, 1.35));
        var distance = 45u + point * 3u + (uint)(mapIndex * 20);
        return new SectorInfo(point, $"M{mapIndex + 1}", mapIndex + 1, requiredRank, exp, distance);
    }

    private sealed record PartStats(int Surveillance, int Retrieval, int Favor, int Range, int Speed);

    private sealed record SectorInfo(uint Point, string MapCode, uint MapId, int RequiredRank, uint Exp, uint Distance);
}
