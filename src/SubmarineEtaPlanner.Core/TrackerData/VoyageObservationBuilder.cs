using SubmarineEtaPlanner.Planner;

namespace SubmarineEtaPlanner.TrackerData;

internal sealed record VoyageObservationRawRow(
    byte[] FcId,
    long SubmarineId,
    DateTimeOffset ReturnAtUtc,
    uint SectorId,
    int Rank,
    int Surveillance,
    int Retrieval,
    int Favor,
    uint PrimaryItemId,
    long PrimaryItemCount,
    uint AdditionalItemId,
    long AdditionalItemCount)
{
    public string FcIdKey => Convert.ToHexString(FcId);
}

internal static class VoyageObservationBuilder
{
    public static IReadOnlyList<VoyageObservation> Build(
        IEnumerable<VoyageObservationRawRow> rows,
        IReadOnlyList<SalvageItemValue> salvageItems,
        ICollection<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(salvageItems);
        ArgumentNullException.ThrowIfNull(warnings);

        var itemValues = salvageItems.ToDictionary(item => item.ItemId);
        return rows
            .Select(row => new NormalizedRow(row, row.ReturnAtUtc.ToUniversalTime()))
            .GroupBy(row => new VoyageKey(
                row.Row.FcIdKey,
                row.Row.SubmarineId,
                row.ReturnAtUtc))
            .OrderBy(group => group.Key.FcIdKey, StringComparer.Ordinal)
            .ThenBy(group => group.Key.SubmarineId)
            .ThenBy(group => group.Key.ReturnAtUtc)
            .Select(group => BuildObservation(group.Key, group, itemValues, warnings))
            .ToArray();
    }

    private static VoyageObservation BuildObservation(
        VoyageKey key,
        IEnumerable<NormalizedRow> rows,
        IReadOnlyDictionary<uint, SalvageItemValue> itemValues,
        ICollection<string> warnings)
    {
        var voyageRows = rows.ToArray();
        var first = voyageRows[0].Row;
        var retainedStats = VoyageStats.From(first);
        var conflictingStats = voyageRows
            .Select(row => VoyageStats.From(row.Row))
            .Where(stats => stats != retainedStats)
            .Distinct()
            .ToArray();
        if (conflictingStats.Length > 0)
        {
            warnings.Add(
                $"Historical voyage {key.FcIdKey}/{key.SubmarineId} returning at {key.ReturnAtUtc:O} " +
                $"has inconsistent rank or stats; retained the first values " +
                $"(rank {retainedStats.Rank}, surveillance {retainedStats.Surveillance}, " +
                $"retrieval {retainedStats.Retrieval}, favor {retainedStats.Favor}) and observed " +
                string.Join(
                    ", ",
                    conflictingStats.Select(stats =>
                        $"(rank {stats.Rank}, surveillance {stats.Surveillance}, " +
                        $"retrieval {stats.Retrieval}, favor {stats.Favor})")) +
                ".");
        }

        var quantities = new Dictionary<uint, long>();
        foreach (var row in voyageRows)
        {
            AddItem(quantities, itemValues, row.Row.PrimaryItemId, row.Row.PrimaryItemCount);
            AddItem(quantities, itemValues, row.Row.AdditionalItemId, row.Row.AdditionalItemCount);
        }

        var items = quantities
            .OrderBy(pair => pair.Key)
            .Select(pair =>
            {
                var item = itemValues[pair.Key];
                return new SalvageItemTotal(item.ItemId, item.Name, item.NpcSalePrice, pair.Value);
            })
            .ToArray();

        return new VoyageObservation(
            key.FcIdKey,
            FreeCompanyIdDecoder.TryDecode(first.FcId),
            key.SubmarineId,
            key.ReturnAtUtc,
            voyageRows.Select(row => row.Row.SectorId).Distinct().Order().ToArray(),
            retainedStats.Rank,
            retainedStats.Surveillance,
            retainedStats.Retrieval,
            retainedStats.Favor,
            items);
    }

    private static void AddItem(
        IDictionary<uint, long> quantities,
        IReadOnlyDictionary<uint, SalvageItemValue> itemValues,
        uint itemId,
        long quantity)
    {
        if (quantity <= 0 || !itemValues.ContainsKey(itemId))
            return;

        quantities.TryGetValue(itemId, out var currentQuantity);
        quantities[itemId] = checked(currentQuantity + quantity);
    }

    private readonly record struct VoyageKey(
        string FcIdKey,
        long SubmarineId,
        DateTimeOffset ReturnAtUtc);

    private readonly record struct VoyageStats(
        int Rank,
        int Surveillance,
        int Retrieval,
        int Favor)
    {
        public static VoyageStats From(VoyageObservationRawRow row)
            => new(row.Rank, row.Surveillance, row.Retrieval, row.Favor);
    }

    private readonly record struct NormalizedRow(
        VoyageObservationRawRow Row,
        DateTimeOffset ReturnAtUtc);
}
