using SubmarineEtaPlanner.Planner;

namespace SubmarineEtaPlanner;

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

    public ulong? FuelHolderCharacterId { get; set; }

    public int? ManualCeruleumTanks { get; set; }

    public int? CeruleumReserve { get; set; }

    public Dictionary<long, SubmarinePreferences> Submarines { get; set; } = [];
}

internal static class FcPreferencesMigration
{
    public static bool Normalize(FcPreferences preferences)
    {
        var changed = false;

        if (preferences.ManualCeruleumTanks is < 0)
        {
            preferences.ManualCeruleumTanks = 0;
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
            return true;
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
