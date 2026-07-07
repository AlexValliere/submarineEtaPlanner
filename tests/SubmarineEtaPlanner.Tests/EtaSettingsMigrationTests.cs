using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class EtaSettingsMigrationTests
{
    [Fact]
    public void DefaultsUseAverageExpAndFastestLevelingRouteGoal()
    {
        var settings = EtaSettings.CreateDefault();

        Assert.Equal(ExpMode.Average, settings.ExpMode);
        Assert.Equal(RouteGoal.FastestLevelingOnly, settings.RouteGoal);
        Assert.Equal(EtaModel.PracticalLeveling, settings.EtaModel);
        Assert.Equal(24, settings.PracticalMaxVoyageHours);
        Assert.Equal(TimeoutResultBehavior.KeepLastComplete, settings.TimeoutResultBehavior);
        Assert.False(settings.EffectiveOptimizeExpPerHour);
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
        Assert.Equal(RouteGoal.FastestLevelingOnly, settings.RouteGoal);
        Assert.Equal(EtaModel.PracticalLeveling, settings.EtaModel);
        Assert.Equal(24, settings.PracticalMaxVoyageHours);
        Assert.Equal(TimeoutResultBehavior.KeepLastComplete, settings.TimeoutResultBehavior);
        Assert.False(settings.OptimizeExpPerHour);
        Assert.False(settings.EffectiveOptimizeExpPerHour);
        Assert.Equal(120, settings.TargetRank);
        Assert.Equal("custom.db", settings.SubmarineTrackerDatabasePathOverride);
    }
}
