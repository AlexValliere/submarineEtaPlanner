namespace SubmarineEtaPlanner.TrackerData;

public sealed record SalvageItemValue(uint ItemId, string Name, uint NpcSalePrice);

public interface ISalvageValueCatalog
{
    IReadOnlyList<SalvageItemValue> Items { get; }
}

public sealed class KnownSalvageValueCatalog : ISalvageValueCatalog
{
    public static KnownSalvageValueCatalog Instance { get; } = new();

    private KnownSalvageValueCatalog()
    {
    }

    public IReadOnlyList<SalvageItemValue> Items { get; } =
    [
        new(22500, "Salvaged Ring", 8_000),
        new(22501, "Salvaged Bracelet", 9_000),
        new(22502, "Salvaged Earring", 10_000),
        new(22503, "Salvaged Necklace", 13_000),
        new(22504, "Extravagant Salvaged Ring", 27_000),
        new(22505, "Extravagant Salvaged Bracelet", 28_500),
        new(22506, "Extravagant Salvaged Earring", 30_000),
        new(22507, "Extravagant Salvaged Necklace", 34_500),
    ];
}
