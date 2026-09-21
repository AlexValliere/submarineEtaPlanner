namespace SubmarineEtaPlanner.Planner;

/// <summary>History snapshots and saved inputs drive matching, never sort order or the displayed FC scope.</summary>
internal sealed class IncomeProjectionCache
{
    private IReadOnlyList<FcState>? source;
    private ISubmarineCatalog? catalog;
    private IRouteOperationalCatalog? operationalCatalog;
    private string? settingsKey;
    private IncomeProjectionResult? result;
    internal int BuildCount { get; private set; }

    public IncomeProjectionResult Get(IReadOnlyList<FcState> snapshot,
        IReadOnlyDictionary<string, FcPreferences> preferences, EtaSettings settings,
        ISubmarineCatalog submarineCatalog, IRouteOperationalCatalog routeCatalog, DateTimeOffset now)
    {
        var key = FcDataFingerprint.Hash(writer =>
        {
            writer.Write(settings.TargetRank);
            writer.Write(settings.CollectionDelayMinutes);
            writer.Write(submarineCatalog.MaximumRank);
            foreach (var state in snapshot.OrderBy(fc => fc.FcIdKey, StringComparer.OrdinalIgnoreCase))
            {
                var fc = preferences.GetValueOrDefault(state.FcIdKey) ?? new FcPreferences();
                writer.Write(state.FcIdKey.ToUpperInvariant());
                writer.Write(fc.Hidden);
                writer.Write(fc.TargetRankOverride ?? -1);
                writer.Write(fc.StrategyOverride is { } strategy ? (int)strategy : -1);
                writer.Write(state.Submarines.Count);
                foreach (var submarine in state.Submarines.OrderBy(submarine => submarine.SubmarineId))
                {
                    var sub = fc.Submarines.GetValueOrDefault(submarine.SubmarineId) ?? new SubmarinePreferences();
                    writer.Write(submarine.SubmarineId);
                    writer.Write((int)sub.Assignment);
                    writer.Write(sub.CollectionDelayMinutes ?? -1);
                    writer.Write(sub.PinnedFarmingRoute?.Count ?? 0);
                    foreach (var sector in sub.PinnedFarmingRoute ?? []) writer.Write(sector);
                }
            }
        });
        if (this.result is not null && ReferenceEquals(this.source, snapshot) && this.settingsKey == key
            && ReferenceEquals(this.catalog, submarineCatalog) && ReferenceEquals(this.operationalCatalog, routeCatalog)
            && now >= this.result.GeneratedAtUtc && now < this.result.NextRefreshAtUtc)
            return this.result;

        this.result = IncomeProjectionCalculator.Calculate(snapshot, preferences, settings, submarineCatalog, routeCatalog, now);
        this.source = snapshot;
        this.catalog = submarineCatalog;
        this.operationalCatalog = routeCatalog;
        this.settingsKey = key;
        this.BuildCount++;
        return this.result;
    }
}
