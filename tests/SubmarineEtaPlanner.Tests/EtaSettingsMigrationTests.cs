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
    }

    [Fact]
    public void VersionTwoMigrationDefaultsExistingConfigsToRealisticEtaSettings()
    {
        var version = 1;
        var settings = EtaSettings.CreateDefault();
        settings.TargetRank = 120;
        settings.ExpMode = ExpMode.Guaranteed;
        settings.RouteGoal = RouteGoal.UnlockEverythingThenLevel;
        settings.SubmarineTrackerDatabasePathOverride = "custom.db";

        var changed = EtaSettingsMigration.Migrate(settings, ref version);

        Assert.True(changed);
        Assert.Equal(EtaSettingsMigration.CurrentVersion, version);
        Assert.Equal(ExpMode.Average, settings.ExpMode);
        Assert.Equal(RouteGoal.FastestLevelingOnly, settings.RouteGoal);
        Assert.Equal(120, settings.TargetRank);
        Assert.Equal("custom.db", settings.SubmarineTrackerDatabasePathOverride);
    }
}
