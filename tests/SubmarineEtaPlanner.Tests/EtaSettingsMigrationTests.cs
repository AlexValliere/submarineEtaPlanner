using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class EtaSettingsMigrationTests
{
    [Fact]
    public void DefaultsUseAverageExpAndUnlockLevelingRouteGoal()
    {
        var settings = EtaSettings.CreateDefault();

        Assert.Equal(ExpMode.Average, settings.ExpMode);
        Assert.Equal(RouteGoal.UnlockLevelingRoutesThenLevel, settings.RouteGoal);
        Assert.Equal(RouteGoal.UnlockLevelingRoutesThenLevel, settings.GetEffectiveRouteGoal());
        Assert.Equal(EtaModel.PracticalLeveling, settings.EtaModel);
        Assert.Equal(0, settings.PracticalMaxVoyageHours);
        Assert.Equal(TimeoutResultBehavior.KeepLastComplete, settings.TimeoutResultBehavior);
        Assert.True(settings.GetEffectiveOptimizeExpPerHour());
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

}
