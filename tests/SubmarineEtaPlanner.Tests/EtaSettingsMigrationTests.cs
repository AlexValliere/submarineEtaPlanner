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
        Assert.False(settings.GetEffectiveOptimizeExpPerHour());
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
        Assert.False(settings.GetEffectiveOptimizeExpPerHour());
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
        Assert.Equal(4, settings.BuildProfile.Count);

        changed = EtaSettingsMigration.Migrate(settings, ref version);
        Assert.False(changed);
        Assert.Equal(4, settings.BuildProfile.Count);
    }

    [Fact]
    public void VersionSevenMigrationPreservesLegacyMrojzReadinessPreference()
    {
        var version = 6;
        var settings = EtaSettings.CreateDefault();
        settings.ShowPost114MrojzReadiness = false;

        var changed = EtaSettingsMigration.Migrate(settings, ref version);

        Assert.True(changed);
        Assert.Equal(EtaSettingsMigration.CurrentVersion, version);
        Assert.False(settings.ShowMrojzReadiness);
        Assert.Null(settings.ShowPost114MrojzReadiness);
    }
}
