using System.Text.Json;
using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class IncomeProjectionConfigurationTests
{
    [Fact]
    public void ExistingConfigurationDefaultsToHistoryAndPreservesHistoryPreferences()
    {
        var config = JsonSerializer.Deserialize<Configuration>("""
            { "Version": 13, "IncomePeriod": 2, "IncomeSort": 3, "IncomeView": 0, "ShowIncomeChart": false }
            """)!;
        config.Migrate();
        Assert.Equal(IncomeDisplayMode.History, config.IncomeDisplayMode);
        Assert.Equal(IncomeProjectionHorizon.Days365, config.IncomeProjectionHorizon);
        Assert.True(config.ShowIncomeProjectionChart);
        Assert.True(config.AllowPreviousRankIncomeApproximations);
        Assert.Equal(IncomePeriod.Days90, config.IncomePeriod);
        Assert.Equal(IncomeSort.FcName, config.IncomeSort);
        Assert.Equal(IncomeView.AllFleets, config.IncomeView);
        Assert.False(config.ShowIncomeChart);
    }

    [Fact]
    public void InvalidValuesNormalizeAndSavedProjectionPreferencesRoundTrip()
    {
        var config = new Configuration { IncomeDisplayMode = (IncomeDisplayMode)99, IncomeProjectionHorizon = (IncomeProjectionHorizon)12 };
        Assert.True(config.Migrate());
        Assert.Equal(IncomeDisplayMode.History, config.IncomeDisplayMode);
        Assert.Equal(IncomeProjectionHorizon.Days365, config.IncomeProjectionHorizon);
        config.IncomeDisplayMode = IncomeDisplayMode.Projection;
        config.IncomeProjectionHorizon = IncomeProjectionHorizon.Days90;
        config.ShowIncomeProjectionChart = false;
        config.AllowPreviousRankIncomeApproximations = false;
        var restored = JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(config))!;
        restored.Migrate(); // Deserialization restores the default dictionary comparer; migration normalizes it.
        Assert.False(restored.Migrate());
        Assert.Equal(IncomeDisplayMode.Projection, restored.IncomeDisplayMode);
        Assert.Equal(IncomeProjectionHorizon.Days90, restored.IncomeProjectionHorizon);
        Assert.False(restored.ShowIncomeProjectionChart);
        Assert.False(restored.AllowPreviousRankIncomeApproximations);
        Assert.True(restored.ShowIncomeChart);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ApproximationPreferenceRoundTripsIndependentlyOfSettings(bool enabled)
    {
        var config = new Configuration { AllowPreviousRankIncomeApproximations = enabled };
        config.Migrate();
        config.Settings.CollectionDelayMinutes = 240;
        var restored = JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(config))!;
        restored.Migrate();
        Assert.Equal(enabled, restored.AllowPreviousRankIncomeApproximations);
        Assert.Equal(240, restored.Settings.CollectionDelayMinutes);
        Assert.Equal(IncomeDisplayMode.History, restored.IncomeDisplayMode);
    }

}
