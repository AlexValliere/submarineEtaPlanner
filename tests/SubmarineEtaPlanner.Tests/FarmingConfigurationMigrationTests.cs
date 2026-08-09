using System.Text.Json;
using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class FarmingConfigurationMigrationTests
{
    [Fact]
    public void VersionElevenConfigurationMigratesToVersionTwelveAndPreservesExistingPreferences()
    {
        var version = 11;
        var settings = EtaSettings.CreateDefault();
        var preferences = new FcPreferences
        {
            Favorite = true,
            TargetRankOverride = 117,
            StrategyOverride = FcStrategyPreset.SlotsFirstThenImmediateExp,
        };

        var settingsChanged = EtaSettingsMigration.Migrate(settings, ref version);
        var preferencesChanged = FcPreferencesMigration.Normalize(preferences);

        Assert.True(settingsChanged);
        Assert.False(preferencesChanged);
        Assert.Equal(12, version);
        Assert.True(preferences.Favorite);
        Assert.Equal(117, preferences.TargetRankOverride);
        Assert.Equal(FcStrategyPreset.SlotsFirstThenImmediateExp, preferences.StrategyOverride);
        Assert.Empty(preferences.Submarines);
    }

    [Fact]
    public void NullSubmarineDictionaryBecomesEmptyWithoutCreatingSubmarineEntries()
    {
        var preferences = new FcPreferences { Submarines = null! };

        Assert.True(FcPreferencesMigration.Normalize(preferences));

        Assert.NotNull(preferences.Submarines);
        Assert.Empty(preferences.Submarines);
    }

    [Fact]
    public void MigrationNormalizesFarmingPreferencesAndPreservesRouteOrder()
    {
        var preferences = new FcPreferences
        {
            ManualCeruleumTanks = -50,
            CeruleumReserve = -1,
            Submarines = new Dictionary<long, SubmarinePreferences>
            {
                [1001] = new SubmarinePreferences
                {
                    Assignment = (SubmarineAssignment)999,
                    PinnedFarmingRoute = [8, 0, 3, 8, 5, 3],
                    CollectionDelayMinutes = -10,
                },
            },
        };

        Assert.True(FcPreferencesMigration.Normalize(preferences));

        Assert.Equal(0, preferences.ManualCeruleumTanks);
        Assert.Equal(0, preferences.CeruleumReserve);
        var submarine = Assert.Single(preferences.Submarines).Value;
        Assert.Equal(SubmarineAssignment.Auto, submarine.Assignment);
        Assert.Equal([8u, 3u, 5u], submarine.PinnedFarmingRoute);
        Assert.Equal(0, submarine.CollectionDelayMinutes);
    }

    [Fact]
    public void ReRunningMigrationIsIdempotent()
    {
        var version = 11;
        var settings = EtaSettings.CreateDefault();
        var preferences = new FcPreferences
        {
            Submarines = new Dictionary<long, SubmarinePreferences>
            {
                [1001] = new SubmarinePreferences
                {
                    Assignment = (SubmarineAssignment)(-1),
                    PinnedFarmingRoute = [4, 0, 4, 2],
                    CollectionDelayMinutes = -1,
                },
            },
        };

        Assert.True(EtaSettingsMigration.Migrate(settings, ref version));
        Assert.True(FcPreferencesMigration.Normalize(preferences));

        Assert.False(EtaSettingsMigration.Migrate(settings, ref version));
        Assert.False(FcPreferencesMigration.Normalize(preferences));
        Assert.Equal(12, version);
        Assert.Equal([4u, 2u], preferences.Submarines[1001].PinnedFarmingRoute);
    }

    [Fact]
    public void NewFarmingPropertiesSerializeAndDeserialize()
    {
        var preferences = new FcPreferences
        {
            FuelHolderCharacterId = 76561198000000001,
            ManualCeruleumTanks = 450,
            CeruleumReserve = 90,
            Submarines = new Dictionary<long, SubmarinePreferences>
            {
                [1001] = new SubmarinePreferences
                {
                    Assignment = SubmarineAssignment.Farming,
                    PinnedFarmingRoute = [7, 12, 18],
                    CollectionDelayMinutes = 25,
                },
            },
        };

        var json = JsonSerializer.Serialize(preferences);
        var restored = JsonSerializer.Deserialize<FcPreferences>(json);

        Assert.NotNull(restored);
        Assert.Equal(preferences.FuelHolderCharacterId, restored.FuelHolderCharacterId);
        Assert.Equal(450, restored.ManualCeruleumTanks);
        Assert.Equal(90, restored.CeruleumReserve);
        var submarine = Assert.Single(restored.Submarines).Value;
        Assert.Equal(SubmarineAssignment.Farming, submarine.Assignment);
        Assert.Equal([7u, 12u, 18u], submarine.PinnedFarmingRoute);
        Assert.Equal(25, submarine.CollectionDelayMinutes);
    }

    [Fact]
    public void SimulationOverrideContainsOnlyExplicitAssignments()
    {
        var preferences = new FcPreferences
        {
            Submarines = new Dictionary<long, SubmarinePreferences>
            {
                [1] = new() { Assignment = SubmarineAssignment.Auto },
                [2] = new() { Assignment = SubmarineAssignment.Farming, PinnedFarmingRoute = [7, 8] },
                [3] = new() { Assignment = SubmarineAssignment.Paused },
            },
            FuelHolderCharacterId = 100,
            ManualCeruleumTanks = 200,
            CeruleumReserve = 50,
        };

        var simulationOverride = FcSimulationOverride.FromPreferences(preferences);

        Assert.NotNull(simulationOverride);
        Assert.Equal(
            new Dictionary<long, SubmarineAssignment>
            {
                [2] = SubmarineAssignment.Farming,
                [3] = SubmarineAssignment.Paused,
            },
            simulationOverride.SubmarineAssignments);
    }

    [Fact]
    public void PinnedRouteAndInventoryDoNotAffectEtaFingerprint()
    {
        var before = new FcPreferences
        {
            Submarines = new Dictionary<long, SubmarinePreferences>
            {
                [2] = new() { Assignment = SubmarineAssignment.Farming, PinnedFarmingRoute = [7, 8] },
            },
            FuelHolderCharacterId = 100,
            ManualCeruleumTanks = 200,
            CeruleumReserve = 50,
        };
        var after = new FcPreferences
        {
            Submarines = new Dictionary<long, SubmarinePreferences>
            {
                [2] = new() { Assignment = SubmarineAssignment.Farming, PinnedFarmingRoute = [10, 11] },
            },
            FuelHolderCharacterId = 999,
            ManualCeruleumTanks = 1,
            CeruleumReserve = 500,
        };
        var settings = EtaSettings.CreateDefault();

        var beforeFingerprint = CalculationSettingsFingerprint.Create(
            settings,
            FcSimulationOverride.FromPreferences(before)!.SubmarineAssignments);
        var afterFingerprint = CalculationSettingsFingerprint.Create(
            settings,
            FcSimulationOverride.FromPreferences(after)!.SubmarineAssignments);

        Assert.Equal(beforeFingerprint, afterFingerprint);
    }
}
