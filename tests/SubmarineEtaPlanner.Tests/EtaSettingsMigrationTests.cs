using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class EtaSettingsMigrationTests
{
    [Fact]
    public void DefaultsUseAverageExpAndUnlockLevelingRouteGoal()
    {
        var settings = EtaSettings.CreateDefault();

        Assert.Equal(90, settings.TargetRank);
        Assert.Equal(ExpMode.Average, settings.ExpMode);
        Assert.Equal(120, settings.CollectionDelayMinutes);
        Assert.Equal(SimulationMode.Fleet, settings.SimulationMode);
        Assert.Equal(RouteGoal.UnlockLevelingRoutesThenLevel, settings.RouteGoal);
        Assert.Equal(RouteGoal.UnlockLevelingRoutesThenLevel, settings.GetEffectiveRouteGoal());
        Assert.Equal(EtaModel.PracticalLeveling, settings.EtaModel);
        Assert.Equal(0, settings.PracticalMaxVoyageHours);
        Assert.Equal(0, settings.DurationLimitHours);
        Assert.True(settings.PrioritizeSubSlots);
        Assert.Equal(TimeoutResultBehavior.KeepLastComplete, settings.TimeoutResultBehavior);
        Assert.True(settings.ShowRouteDiagnostics);
        Assert.True(settings.GetEffectiveOptimizeExpPerHour());
        Assert.Equal(0.33, settings.UnlockSuccessProbability, 2);
        Assert.Equal(20, settings.CalculationTimeLimitSeconds);
        Assert.Equal(500, settings.SimulationSafetyVoyageCapPerSubmarine);
        Assert.Equal(
            [
                new BuildProfileStep(1, 14, "SSSS"),
                new BuildProfileStep(15, 24, "SSUS"),
                new BuildProfileStep(25, 999, "SSUW"),
            ],
            settings.BuildProfile);
        Assert.Empty(settings.ManualCurrentRouteOverrides);
        Assert.Null(settings.SubmarineTrackerDatabasePathOverride);
    }

    [Fact]
    public void CurrentVersionMigrationPreservesExistingUserSettings()
    {
        var version = EtaSettingsMigration.CurrentVersion;
        var settings = EtaSettings.CreateDefault() with
        {
            TargetRank = 114,
            CollectionDelayMinutes = 15,
            SimulationMode = SimulationMode.OptimisticPerSub,
            EtaModel = EtaModel.ExactRouteSearch,
            PracticalMaxVoyageHours = 48,
            PrioritizeSubSlots = false,
            CalculationTimeLimitSeconds = 20,
            SimulationSafetyVoyageCapPerSubmarine = 900,
            ShowRouteDiagnostics = false,
            TimeoutResultBehavior = TimeoutResultBehavior.ShowPartial,
            UnlockSuccessProbability = 0.5,
            BuildProfile = [new BuildProfileStep(1, 999, "CCCC")],
            SubmarineTrackerDatabasePathOverride = "custom.db",
            ManualCurrentRouteOverrides = new Dictionary<string, List<uint>> { ["sub"] = [1, 2, 3] },
        };

        var changed = EtaSettingsMigration.Migrate(settings, ref version);

        Assert.False(changed);
        Assert.Equal(114, settings.TargetRank);
        Assert.Equal(15, settings.CollectionDelayMinutes);
        Assert.Equal(SimulationMode.OptimisticPerSub, settings.SimulationMode);
        Assert.Equal(EtaModel.ExactRouteSearch, settings.EtaModel);
        Assert.Equal(48, settings.PracticalMaxVoyageHours);
        Assert.False(settings.PrioritizeSubSlots);
        Assert.Equal(20, settings.CalculationTimeLimitSeconds);
        Assert.Equal(900, settings.SimulationSafetyVoyageCapPerSubmarine);
        Assert.False(settings.ShowRouteDiagnostics);
        Assert.Equal(TimeoutResultBehavior.ShowPartial, settings.TimeoutResultBehavior);
        Assert.Equal(0.5, settings.UnlockSuccessProbability);
        Assert.Equal([new BuildProfileStep(1, 999, "CCCC")], settings.BuildProfile);
        Assert.Equal("custom.db", settings.SubmarineTrackerDatabasePathOverride);
        Assert.Equal([1u, 2u, 3u], settings.ManualCurrentRouteOverrides["sub"]);
    }

    [Fact]
    public void VersionThreeMigrationDefaultsExistingConfigsToPracticalEtaSettings()
    {
        var version = 1;
        var settings = EtaSettings.CreateDefault();
        settings.TargetRank = 120;
        settings.ExpMode = ExpMode.Guaranteed;
        settings.RouteGoal = RouteGoal.UnlockEverythingThenLevel;
        settings.EtaModel = EtaModel.ExactRouteSearch;
        settings.PracticalMaxVoyageHours = 0;
        settings.TimeoutResultBehavior = TimeoutResultBehavior.ShowPartial;
        settings.SubmarineTrackerDatabasePathOverride = "custom.db";

        var changed = EtaSettingsMigration.Migrate(settings, ref version);

        Assert.True(changed);
        Assert.Equal(EtaSettingsMigration.CurrentVersion, version);
        Assert.Equal(ExpMode.Average, settings.ExpMode);
        Assert.Equal(RouteGoal.UnlockLevelingRoutesThenLevel, settings.RouteGoal);
        Assert.Equal(RouteGoal.UnlockLevelingRoutesThenLevel, settings.GetEffectiveRouteGoal());
        Assert.Equal(EtaModel.PracticalLeveling, settings.EtaModel);
        Assert.Equal(0, settings.PracticalMaxVoyageHours);
        Assert.Equal(TimeoutResultBehavior.KeepLastComplete, settings.TimeoutResultBehavior);
        Assert.False(settings.OptimizeExpPerHour);
        Assert.True(settings.GetEffectiveOptimizeExpPerHour());
        Assert.Equal(120, settings.TargetRank);
        Assert.Equal("custom.db", settings.SubmarineTrackerDatabasePathOverride);
    }

    [Fact]
    public void VersionSixMigrationRemovesDuplicateBuildRowsAndResetsDurationCap()
    {
        var version = 5;
        var defaults = EtaSettings.CreateDefault().BuildProfile;
        var settings = EtaSettings.CreateDefault();
        settings.PracticalMaxVoyageHours = 48;
        settings.BuildProfile = defaults.Concat(defaults).Concat(defaults).ToList();

        var changed = EtaSettingsMigration.Migrate(settings, ref version);

        Assert.True(changed);
        Assert.Equal(EtaSettingsMigration.CurrentVersion, version);
        Assert.Equal(0, settings.PracticalMaxVoyageHours);
        Assert.Equal(3, settings.BuildProfile.Count);

        changed = EtaSettingsMigration.Migrate(settings, ref version);
        Assert.False(changed);
        Assert.Equal(3, settings.BuildProfile.Count);
    }

    [Fact]
    public void VersionEightMigrationReplacesOnlyLegacyFarmingBuildProfile()
    {
        var version = 7;
        var settings = EtaSettings.CreateDefault();
        settings.BuildProfile =
        [
            new BuildProfileStep(1, 14, "SSSS"),
            new BuildProfileStep(15, 24, "SSUS"),
            new BuildProfileStep(25, 113, "SSUW"),
            new BuildProfileStep(114, 999, "WSCC"),
        ];

        var changed = EtaSettingsMigration.Migrate(settings, ref version);

        Assert.True(changed);
        Assert.Equal(EtaSettingsMigration.CurrentVersion, version);
        Assert.Equal(3, settings.BuildProfile.Count);
        Assert.Equal(new BuildProfileStep(25, 999, "SSUW"), settings.BuildProfile[^1]);
    }

    [Fact]
    public void VersionEightMigrationPreservesCustomBuildProfile()
    {
        var version = 7;
        var settings = EtaSettings.CreateDefault();
        settings.BuildProfile = [new BuildProfileStep(1, 999, "CCCC")];

        EtaSettingsMigration.Migrate(settings, ref version);

        Assert.Equal([new BuildProfileStep(1, 999, "CCCC")], settings.BuildProfile);
    }

    [Fact]
    public void VersionNineMigrationAddsAndClampsUnlockProbability()
    {
        var version = 8;
        var settings = EtaSettings.CreateDefault();
        settings.UnlockSuccessProbability = 0;

        Assert.True(EtaSettingsMigration.Migrate(settings, ref version));
        Assert.Equal(EtaSettingsMigration.CurrentVersion, version);
        Assert.Equal(0.33, settings.UnlockSuccessProbability, 2);

        settings.UnlockSuccessProbability = 5;
        version = 9;
        Assert.True(EtaSettingsMigration.Migrate(settings, ref version));
        Assert.Equal(1.0, settings.UnlockSuccessProbability);
    }

}
