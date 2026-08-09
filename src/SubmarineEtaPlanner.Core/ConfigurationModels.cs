using SubmarineEtaPlanner.Planner;

namespace SubmarineEtaPlanner;

public enum FuelStockMode
{
    Automatic = 0,
    Character = 1,
    Manual = 2,
}

public enum SubmarineAssignment
{
    Auto = 0,
    Leveling = 1,
    Farming = 2,
    Paused = 3,
}

[Serializable]
public sealed class SubmarinePreferences
{
    public SubmarineAssignment Assignment { get; set; } = SubmarineAssignment.Auto;

    public List<uint>? PinnedFarmingRoute { get; set; }

    public int? CollectionDelayMinutes { get; set; }
}

[Serializable]
public sealed class FcPreferences
{
    public bool Favorite { get; set; }

    public int? TargetRankOverride { get; set; }

    public FcStrategyPreset? StrategyOverride { get; set; }

    /// <summary>
    /// The sole authority for selecting the fuel-stock source. Automatic ignores both saved values, resolves
    /// dynamically, and never persists a selected character. Character uses only the saved holder without
    /// fallback; Manual uses only the saved tank count. Changing modes preserves both inactive saved values.
    /// </summary>
    public FuelStockMode FuelStockMode { get; set; } = FuelStockMode.Automatic;

    /// <summary>
    /// The explicitly selected holder used only in Character mode. Automatic and Manual modes ignore it.
    /// </summary>
    public ulong? FuelHolderCharacterId { get; set; }

    /// <summary>
    /// The saved tank count used only in Manual mode. This remains nullable for version-12 JSON compatibility;
    /// consumers should use <c>GetValueOrDefault()</c> after normalization.
    /// </summary>
    public int? ManualCeruleumTanks { get; set; } = 0;

    public int? CeruleumReserve { get; set; }

    public Dictionary<long, SubmarinePreferences> Submarines { get; set; } = [];
}

internal static class FcPreferencesMigration
{
    public static bool Normalize(FcPreferences preferences)
    {
        var changed = false;

        if (!Enum.IsDefined(preferences.FuelStockMode))
        {
            preferences.FuelStockMode = FuelStockMode.Automatic;
            changed = true;
        }

        if (preferences.ManualCeruleumTanks is null or < 0)
        {
            preferences.ManualCeruleumTanks = 0;
            changed = true;
        }

        if (preferences.FuelHolderCharacterId == 0)
        {
            preferences.FuelHolderCharacterId = null;
            changed = true;
        }

        if (preferences.CeruleumReserve is < 0)
        {
            preferences.CeruleumReserve = 0;
            changed = true;
        }

        if (preferences.Submarines is null)
        {
            preferences.Submarines = [];
            changed = true;
        }

        foreach (var contentId in preferences.Submarines.Keys.ToArray())
        {
            var submarine = preferences.Submarines[contentId];
            if (submarine is null)
            {
                preferences.Submarines[contentId] = new SubmarinePreferences();
                changed = true;
                continue;
            }

            if (!Enum.IsDefined(submarine.Assignment))
            {
                submarine.Assignment = SubmarineAssignment.Auto;
                changed = true;
            }

            if (submarine.PinnedFarmingRoute is { } route)
            {
                var normalizedRoute = route.Where(sectorId => sectorId != 0).Distinct().ToList();
                if (!route.SequenceEqual(normalizedRoute))
                {
                    submarine.PinnedFarmingRoute = normalizedRoute;
                    changed = true;
                }
            }

            if (submarine.CollectionDelayMinutes is < 0)
            {
                submarine.CollectionDelayMinutes = 0;
                changed = true;
            }
        }

        return changed;
    }
}
