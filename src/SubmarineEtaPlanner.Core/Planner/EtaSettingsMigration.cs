namespace SubmarineEtaPlanner.Planner;

public static class EtaSettingsMigration
{
    public const int CurrentVersion = 4;

    public static bool Migrate(EtaSettings settings, ref int version)
    {
        if (version >= CurrentVersion)
            return false;

        if (version < 2)
        {
            settings.ExpMode = ExpMode.Average;
            settings.RouteGoal = RouteGoal.FastestLevelingOnly;
        }

        if (version < 3)
        {
            settings.EtaModel = EtaModel.PracticalLeveling;
            settings.ExpMode = ExpMode.Average;
            settings.RouteGoal = RouteGoal.FastestLevelingOnly;
            settings.PracticalMaxVoyageHours = settings.PracticalMaxVoyageHours <= 0 ? 24 : settings.PracticalMaxVoyageHours;
            settings.TimeoutResultBehavior = TimeoutResultBehavior.KeepLastComplete;
            settings.ShowRouteDiagnostics = true;
        }

        if (version < 4 && settings.EtaModel == EtaModel.PracticalLeveling)
            settings.OptimizeExpPerHour = false;

        version = CurrentVersion;
        return true;
    }
}
