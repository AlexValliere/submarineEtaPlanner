namespace SubmarineEtaPlanner.Planner;

public static class FleetPresentationFiltering
{
    public static bool Includes(FcOperationalProjection projection, FleetMode? requiredMode)
        => requiredMode is null || projection.Mode == requiredMode.Value;
}
