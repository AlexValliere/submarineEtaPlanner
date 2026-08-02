namespace SubmarineEtaPlanner.Planner;

public static class EtaSettingsMigration
{
    public const int CurrentVersion = 8;

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

        if (version < 5 && settings.EtaModel == EtaModel.PracticalLeveling)
            settings.RouteGoal = RouteGoal.UnlockLevelingRoutesThenLevel;

        if (version < 6)
        {
            settings.PracticalMaxVoyageHours = 0;
            settings.BuildProfile = settings.BuildProfile
                .DistinctBy(step => (step.MinRank, step.MaxRank, (step.BuildCode ?? string.Empty).ToUpperInvariant()))
                .ToList();
            if (settings.BuildProfile.Count == 0)
                settings.BuildProfile = EtaSettings.CreateDefault().BuildProfile;
        }

        if (version < 8 && IsLegacyFarmingBuildProfile(settings.BuildProfile))
            settings.BuildProfile = EtaSettings.CreateDefault().BuildProfile;

        version = CurrentVersion;
        return true;
    }

    private static bool IsLegacyFarmingBuildProfile(IReadOnlyList<BuildProfileStep> profile)
    {
        BuildProfileStep[] legacyProfile =
        [
            new BuildProfileStep(1, 14, "SSSS"),
            new BuildProfileStep(15, 24, "SSUS"),
            new BuildProfileStep(25, 113, "SSUW"),
            new BuildProfileStep(114, 999, "WSCC"),
        ];
        return profile.Count == legacyProfile.Length &&
               profile.Zip(legacyProfile).All(pair => pair.First == pair.Second);
    }
}
