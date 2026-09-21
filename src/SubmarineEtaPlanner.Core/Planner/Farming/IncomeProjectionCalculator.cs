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
        DateTimeOffset now)
    {
        var saved = preferences.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var visible = source.Where(fc => saved.GetValueOrDefault(fc.FcIdKey)?.Hidden != true).ToArray();
        var windowStart = now.AddDays(-HistoryDays);
        var refreshAt = now.AddMinutes(1);
        var pooled = new Dictionary<SampleKey, SampleAccumulator>();
        var own = new Dictionary<(string FcId, long SubmarineId, SampleKey Setup), SampleAccumulator>();
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
                if (build is not null && route.Route.Count > 0)
                {
                    var key = new SampleKey(SectorSetSignature.Create(route.Route), build.Surveillance, build.Retrieval, build.Favor);
                    sample = own.GetValueOrDefault((fc.FcIdKey.ToUpperInvariant(), submarine.SubmarineId, key));
                    if (sample is { Count: >= MinimumReturns }) sampleSource = IncomeProjectionSampleSource.Own;
                    else sample = pooled.GetValueOrDefault(key);
                }
                var evidence = new IncomeProjectionSample(sampleSource, sample?.Count ?? 0, sample?.FcIds.Count ?? 0,
                    sample?.First, sample?.Last);
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
                    cycle?.CollectionDelay, cycle?.FullCycleDuration, average, perDay, evidence, reason);
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
