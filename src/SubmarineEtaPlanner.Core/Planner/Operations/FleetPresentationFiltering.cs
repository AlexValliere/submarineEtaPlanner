namespace SubmarineEtaPlanner.Planner;

public static class FleetPresentationFiltering
{
    public static bool Includes(FcOperationalProjection projection, FleetMode? requiredMode)
        => requiredMode switch
        {
            null => true,
            FleetMode.Leveling => projection.RoleSummary.HasLeveling,
            FleetMode.Farming => projection.RoleSummary.HasFarming,
            _ => false,
        };
}
