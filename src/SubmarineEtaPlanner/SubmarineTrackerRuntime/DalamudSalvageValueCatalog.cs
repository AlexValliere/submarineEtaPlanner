using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using SubmarineEtaPlanner.TrackerData;

namespace SubmarineEtaPlanner.SubmarineTrackerRuntime;

public sealed class DalamudSalvageValueCatalog : ISalvageValueCatalog
{
    public DalamudSalvageValueCatalog(IDataManager dataManager)
    {
        var itemSheet = dataManager.GetExcelSheet<Item>();
        Items = KnownSalvageValueCatalog.Instance.Items.Select(fallback =>
        {
            if (!itemSheet.TryGetRow(fallback.ItemId, out var item) || item.PriceLow == 0)
                return fallback;

            var name = item.Name.ToString();
            return new SalvageItemValue(
                fallback.ItemId,
                string.IsNullOrWhiteSpace(name) ? fallback.Name : name,
                item.PriceLow);
        }).ToArray();
    }

    public IReadOnlyList<SalvageItemValue> Items { get; }
}
