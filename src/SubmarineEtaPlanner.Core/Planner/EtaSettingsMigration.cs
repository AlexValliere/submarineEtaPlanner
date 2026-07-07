namespace SubmarineEtaPlanner.Planner;

public static class EtaSettingsMigration
{
    public const int CurrentVersion = 2;

    public static bool Migrate(EtaSettings settings, ref int version)
    {
        if (version >= CurrentVersion)
            return false;

        if (version < 2)
        {
            settings.ExpMode = ExpMode.Average;
            settings.RouteGoal = RouteGoal.FastestLevelingOnly;
        }

        version = CurrentVersion;
        return true;
    }
}
