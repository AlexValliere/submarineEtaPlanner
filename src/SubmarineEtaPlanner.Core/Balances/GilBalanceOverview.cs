namespace SubmarineEtaPlanner;

/// <summary>The last known wallets and chests for the complete visible-FC scope.</summary>
public sealed record GilBalanceOverview(
    long? WalletGil,
    long? ChestGil,
    long? RecordedGil,
    int VisibleFcCount,
    int ObservedFcCount,
    int ObservedWalletCount,
    int ObservedChestCount,
    bool IsPartial,
    DateTimeOffset? OldestObservationAtUtc,
    bool ScopeKnown)
{
    public static GilBalanceOverview Create(
        IEnumerable<ulong?> visibleFcIds,
        GilBalanceSnapshot snapshot,
        bool scopeKnown = true)
    {
        ArgumentNullException.ThrowIfNull(visibleFcIds);
        ArgumentNullException.ThrowIfNull(snapshot);
        // Until the tracker supplies its FC list, observations cannot safely be
        // classified as visible, hidden, or outside the planner's scope.
        if (!scopeKnown)
            return new(null, null, null, 0, 0, 0, 0, false, null, false);

        var ids = visibleFcIds.ToArray();
        var knownIds = ids.Where(id => id is > 0).Select(id => id!.Value).ToHashSet();
        var visibleCount = knownIds.Count + ids.Count(id => id is null or 0);
        var normalized = GilBalanceCalculator.Normalize(snapshot);
        // Normalize before filtering: a character whose latest observation moved
        // to a hidden FC must not leave its previous wallet in the visible total.
        var characters = normalized.Characters
            .Where(character => character.FreeCompanyId is > 0 && knownIds.Contains(character.FreeCompanyId.Value))
            .ToArray();
        var chests = normalized.Chests.Where(chest => knownIds.Contains(chest.FreeCompanyId)).ToArray();
        var walletFcIds = characters.Select(character => character.FreeCompanyId!.Value).ToHashSet();
        var observedFcIds = walletFcIds.Concat(chests.Select(chest => chest.FreeCompanyId)).ToHashSet();
        long? wallets = characters.Length == 0 ? null : characters.Sum(character => character.Gil);
        long? chestGil = chests.Length == 0 ? null : chests.Sum(chest => chest.Gil);
        long? recorded = wallets.HasValue || chestGil.HasValue ? (wallets ?? 0) + (chestGil ?? 0) : null;
        var times = characters.Select(character => character.ObservedAtUtc)
            .Concat(chests.Select(chest => chest.ObservedAtUtc)).ToArray();
        return new(wallets, chestGil, recorded, visibleCount, observedFcIds.Count,
            characters.Length, chests.Length,
            visibleCount > 0 && (walletFcIds.Count < visibleCount || chests.Length < visibleCount),
            times.Length == 0 ? null : times.Min(), true);
    }
}
