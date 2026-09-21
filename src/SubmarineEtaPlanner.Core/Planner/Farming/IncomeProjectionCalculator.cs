namespace SubmarineEtaPlanner.Planner;

public static class IncomeProjectionCalculator
{
    public const int HistoryDays = 90;
    public const int MinimumReturns = 10;

    public static IncomeProjectionResult Calculate(
        IReadOnlyList<FcState> source,
        IReadOnlyDictionary<string, FcPreferences> preferences,
        EtaSettings settings,
        ISubmarineCatalog catalog,
        IRouteOperationalCatalog operationalCatalog,
        DateTimeOffset now,
        bool allowPreviousRankFallback = true)
    {
        var saved = preferences.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var visible = source.Where(fc => saved.GetValueOrDefault(fc.FcIdKey)?.Hidden != true).ToArray();
        var windowStart = now.AddDays(-HistoryDays);
        var refreshAt = now.AddMinutes(1);
        var pooled = new Dictionary<SampleKey, SampleAccumulator>();
        var own = new Dictionary<(string FcId, long SubmarineId, SampleKey Setup), SampleAccumulator>();
        var pooledByRank = new Dictionary<(int Rank, SampleKey Setup), SampleAccumulator>();
        var ownByRank = new Dictionary<(string FcId, long SubmarineId, int Rank, SampleKey Setup), SampleAccumulator>();
        var previousBuilds = new Dictionary<(SubmarineBuildParts Parts, int Rank), SubmarineBuild?>();
        var observed = new HashSet<(string FcId, long SubmarineId, DateTimeOffset ReturnAt)>();
        var notices = new List<string>();

        foreach (var fc in visible)
        {
            if (fc.IncomeHistory.Status != IncomeHistoryReadStatus.Available)
                notices.Add($"{fc.DisplayName}: {fc.IncomeHistory.Reason ?? "History availability is unknown."}");
            foreach (var submarine in fc.Submarines)
            foreach (var voyage in submarine.VoyageHistory)
            {
                if (voyage.ReturnAtUtc > now)
                {
                    ConsiderBoundary(voyage.ReturnAtUtc);
                    continue;
                }
                if (voyage.ReturnAtUtc < windowStart) continue;
                ConsiderBoundary(voyage.ReturnAtUtc.AddDays(HistoryDays).AddTicks(1));
                // Identity belongs to the source FC/submarine. Pooling must never count its own returns twice.
                var fcId = fc.FcIdKey.ToUpperInvariant();
                if (!observed.Add((fcId, submarine.SubmarineId, voyage.ReturnAtUtc))) continue;
                var key = new SampleKey(SectorSetSignature.Create(voyage.SectorIds),
                    voyage.Surveillance, voyage.Retrieval, voyage.Favor);
                Add(pooled, key, voyage, fcId);
                Add(own, (fcId, submarine.SubmarineId, key), voyage, fcId);
                if (allowPreviousRankFallback)
                {
                    Add(pooledByRank, (voyage.Rank, key), voyage, fcId);
                    Add(ownByRank, (fcId, submarine.SubmarineId, voyage.Rank, key), voyage, fcId);
                }
            }
        }

        var results = new List<IncomeFcProjection>();
        foreach (var fc in visible)
        {
            var fcPreferences = saved.GetValueOrDefault(fc.FcIdKey) ?? new FcPreferences();
            var effective = EffectiveEtaSettingsResolver.Resolve(settings,
                new FcSimulationOverride(fcPreferences.TargetRankOverride, fcPreferences.StrategyOverride), catalog.MaximumRank);
            var routes = FarmingRoutePlanResolver.Resolve(fc, fcPreferences, effective.TargetRank, catalog, operationalCatalog);
            var cycles = FarmingCyclePlanBuilder.Build(fc,
                routes.Where(route => Delay(route.SubmarineId) >= 0).ToArray(), fcPreferences, effective, now)
                .ToDictionary(cycle => cycle.SubmarineId);
            var submarines = fc.Submarines.ToDictionary(submarine => submarine.SubmarineId);
            var estimates = routes.Select(route =>
            {
                var submarine = submarines[route.SubmarineId];
                var build = catalog.ResolveBuild(submarine.BuildParts, submarine.Rank);
                SampleAccumulator? sample = null;
                var sampleSource = IncomeProjectionSampleSource.Pooled;
                var matchKind = IncomeProjectionMatchKind.ExactStats;
                int? referenceRank = null;
                IncomeProjectionStats? matchedStats = build is null ? null : IncomeProjectionStats.From(build);
                var exactCount = 0;
                int? previousCount = null;
                if (build is not null && route.Route.Count > 0)
                {
                    var sectors = SectorSetSignature.Create(route.Route);
                    var fcId = fc.FcIdKey.ToUpperInvariant();
                    var key = new SampleKey(sectors, build.Surveillance, build.Retrieval, build.Favor);
                    var exactPool = pooled.GetValueOrDefault(key);
                    exactCount = exactPool?.Count ?? 0;
                    sample = own.GetValueOrDefault((fcId, submarine.SubmarineId, key));
                    if (sample is { Count: >= MinimumReturns }) sampleSource = IncomeProjectionSampleSource.Own;
                    else sample = exactPool;

                    if (allowPreviousRankFallback && submarine.Rank > 1)
                    {
                        var previousRank = submarine.Rank - 1;
                        var buildKey = (submarine.BuildParts, previousRank);
                        if (!previousBuilds.TryGetValue(buildKey, out var previousBuild))
                        {
                            previousBuild = catalog.ResolveBuild(submarine.BuildParts, previousRank);
                            previousBuilds.Add(buildKey, previousBuild);
                        }
                        if (previousBuild is not null)
                        {
                            // Compare recorded stats with today's parts at one earlier rank; no historical part IDs are inferred.
                            var previousKey = new SampleKey(sectors, previousBuild.Surveillance, previousBuild.Retrieval, previousBuild.Favor);
                            var previousPool = pooledByRank.GetValueOrDefault((previousRank, previousKey));
                            previousCount = previousPool?.Count ?? 0;
                            if (sample is not { Count: >= MinimumReturns })
                            {
                                var previousOwn = ownByRank.GetValueOrDefault((fcId, submarine.SubmarineId, previousRank, previousKey));
                                var useOwn = previousOwn is { Count: >= MinimumReturns };
                                var previousSample = useOwn ? previousOwn : previousPool;
                                if (previousSample is { Count: >= MinimumReturns })
                                {
                                    sample = previousSample;
                                    sampleSource = useOwn ? IncomeProjectionSampleSource.Own : IncomeProjectionSampleSource.Pooled;
                                    matchKind = IncomeProjectionMatchKind.PreviousRank;
                                    referenceRank = previousRank;
                                    matchedStats = IncomeProjectionStats.From(previousBuild);
                                }
                            }
                        }
                    }
                }
                var evidence = new IncomeProjectionSample(sampleSource, sample?.Count ?? 0, sample?.FcIds.Count ?? 0,
                    sample?.First, sample?.Last, matchKind, referenceRank, matchedStats);
                cycles.TryGetValue(submarine.SubmarineId, out var cycle);
                var reason = route.Warnings.Count > 0 ? string.Join(" ", route.Warnings)
                    : Delay(submarine.SubmarineId) < 0 ? "Collection delay cannot be negative."
                    : cycle is null || cycle.FullCycleDuration <= TimeSpan.Zero ? "Farming cycle duration is unavailable."
                    : evidence.ReturnCount < MinimumReturns ? "Insufficient matching history"
                    : null;
                decimal? average = reason is null ? sample!.Gil / sample.Count : null;
                decimal? perDay = reason is null
                    ? average * TimeSpan.TicksPerDay / cycle!.FullCycleDuration.Ticks : null;
                return new IncomeSubmarineProjection(submarine.SubmarineId, submarine.Name, submarine.Rank,
                    route.Build, Array.AsReadOnly(route.Route.ToArray()), route.Source, route.VoyageDuration,
                    cycle?.CollectionDelay, cycle?.FullCycleDuration, average, perDay, evidence, reason)
                {
                    CurrentStats = build is null ? null : IncomeProjectionStats.From(build),
                    ExactMatchingReturnCount = exactCount,
                    PreviousRankMatchingReturnCount = previousCount,
                };
            }).OrderBy(submarine => submarine.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            results.Add(new(fc.FcIdKey, fc.FreeCompanyTag, fc.World, Array.AsReadOnly(estimates), IncomeProjectionTotals.From(estimates)));

            int Delay(long id) => fcPreferences.Submarines.GetValueOrDefault(id)?.CollectionDelayMinutes
                ?? effective.CollectionDelayMinutes;
        }
        return new(now, refreshAt, results.AsReadOnly(), notices.AsReadOnly());

        void ConsiderBoundary(DateTimeOffset boundary)
        {
            if (boundary > now && boundary < refreshAt) refreshAt = boundary;
        }
    }

    private static void Add<TKey>(Dictionary<TKey, SampleAccumulator> samples, TKey key, VoyageObservation voyage, string fcId)
        where TKey : notnull
    {
        if (!samples.TryGetValue(key, out var value)) samples[key] = value = new();
        value.Count++;
        value.Gil += voyage.GrossNpcGil;
        value.FcIds.Add(fcId);
        if (value.First is null || voyage.ReturnAtUtc < value.First) value.First = voyage.ReturnAtUtc;
        if (value.Last is null || voyage.ReturnAtUtc > value.Last) value.Last = voyage.ReturnAtUtc;
    }

    private readonly record struct SampleKey(SectorSetSignature Sectors, int Surveillance, int Retrieval, int Favor);
    private sealed class SampleAccumulator
    {
        public int Count;
        public decimal Gil;
        public readonly HashSet<string> FcIds = new(StringComparer.OrdinalIgnoreCase);
        public DateTimeOffset? First;
        public DateTimeOffset? Last;
    }
}
