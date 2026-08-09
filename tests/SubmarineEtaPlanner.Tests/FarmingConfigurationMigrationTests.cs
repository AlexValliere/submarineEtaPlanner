using System.Text.Json;
using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class FarmingConfigurationMigrationTests
{
    [Fact]
    public void VersionTwelveConfigurationMigratesToVersionThirteenWithAutomaticFuelMode()
    {
        var version = 12;
        var settings = EtaSettings.CreateDefault();
        var preferences = new FcPreferences
        {
            Favorite = true,
            TargetRankOverride = 117,
            StrategyOverride = FcStrategyPreset.SlotsFirstThenImmediateExp,
            Submarines = new Dictionary<long, SubmarinePreferences>
            {
                [1001] = new()
                {
                    Assignment = SubmarineAssignment.Farming,
                    PinnedFarmingRoute = [8, 3, 5],
                },
            },
        };

        Assert.True(EtaSettingsMigration.Migrate(settings, ref version));
        Assert.False(FcPreferencesMigration.Normalize(preferences));

        Assert.Equal(13, version);
        Assert.Equal(FuelStockMode.Automatic, preferences.FuelStockMode);
        Assert.Equal(0, preferences.ManualCeruleumTanks);
        Assert.Null(preferences.FuelHolderCharacterId);
        Assert.Null(preferences.CeruleumReserve);
        Assert.True(preferences.Favorite);
        Assert.Equal(117, preferences.TargetRankOverride);
        Assert.Equal(FcStrategyPreset.SlotsFirstThenImmediateExp, preferences.StrategyOverride);
        var submarine = Assert.Single(preferences.Submarines).Value;
        Assert.Equal(SubmarineAssignment.Farming, submarine.Assignment);
        Assert.Equal([8u, 3u, 5u], submarine.PinnedFarmingRoute);
    }

    [Fact]
    public void VersionElevenConfigurationAlsoMigratesForwardToVersionThirteen()
    {
        var version = 11;
        var settings = EtaSettings.CreateDefault();

        Assert.True(EtaSettingsMigration.Migrate(settings, ref version));

        Assert.Equal(13, version);
    }

    [Fact]
    public void VersionTwelveJsonContainingNullManualValueRemainsReadable()
    {
        var json = """
        {
          "FuelHolderCharacterId": null,
          "ManualCeruleumTanks": null,
          "CeruleumReserve": null,
          "Submarines": {}
        }
        """;

        var preferences = JsonSerializer.Deserialize<FcPreferences>(json);

        Assert.NotNull(preferences);
        Assert.True(FcPreferencesMigration.Normalize(preferences));
        Assert.Equal(FuelStockMode.Automatic, preferences.FuelStockMode);
        Assert.Equal(0, preferences.ManualCeruleumTanks);
        Assert.Null(preferences.FuelHolderCharacterId);
        Assert.Null(preferences.CeruleumReserve);
    }

    [Fact]
    public void LegacyManualCountIsPreservedWithoutEnablingManualMode()
    {
        var preferences = new FcPreferences { ManualCeruleumTanks = 450 };

        Assert.False(FcPreferencesMigration.Normalize(preferences));

        Assert.Equal(FuelStockMode.Automatic, preferences.FuelStockMode);
        Assert.Equal(450, preferences.ManualCeruleumTanks);
    }

    [Fact]
    public void LegacyFuelHolderIsPreservedWithoutEnablingCharacterMode()
    {
        var preferences = new FcPreferences { FuelHolderCharacterId = 123 };

        Assert.False(FcPreferencesMigration.Normalize(preferences));

        Assert.Equal(FuelStockMode.Automatic, preferences.FuelStockMode);
        Assert.Equal(123ul, preferences.FuelHolderCharacterId);
    }

    [Fact]
    public void InvalidFuelStockModeNormalizesToAutomatic()
    {
        var preferences = new FcPreferences { FuelStockMode = (FuelStockMode)999 };

        Assert.True(FcPreferencesMigration.Normalize(preferences));

        Assert.Equal(FuelStockMode.Automatic, preferences.FuelStockMode);
    }

    [Theory]
    [InlineData(FuelStockMode.Character)]
    [InlineData(FuelStockMode.Manual)]
    public void ExplicitValidFuelStockModeIsPreserved(FuelStockMode mode)
    {
        var preferences = new FcPreferences { FuelStockMode = mode };

        Assert.False(FcPreferencesMigration.Normalize(preferences));

        Assert.Equal(mode, preferences.FuelStockMode);
    }

    [Fact]
    public void ZeroFuelHolderCharacterIdNormalizesToNull()
    {
        var preferences = new FcPreferences { FuelHolderCharacterId = 0 };

        Assert.True(FcPreferencesMigration.Normalize(preferences));

        Assert.Null(preferences.FuelHolderCharacterId);
    }

    [Fact]
    public void NullSubmarineDictionaryRepairDoesNotSkipFuelNormalization()
    {
        var preferences = new FcPreferences
        {
            FuelStockMode = (FuelStockMode)(-1),
            FuelHolderCharacterId = 0,
            ManualCeruleumTanks = null,
            CeruleumReserve = -1,
            Submarines = null!,
        };

        Assert.True(FcPreferencesMigration.Normalize(preferences));

        Assert.Equal(FuelStockMode.Automatic, preferences.FuelStockMode);
        Assert.Null(preferences.FuelHolderCharacterId);
        Assert.Equal(0, preferences.ManualCeruleumTanks);
        Assert.Equal(0, preferences.CeruleumReserve);
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
                [1001] = new()
                {
                    Assignment = (SubmarineAssignment)999,
                    PinnedFarmingRoute = [8, 0, 3, 8, 5, 3],
                    CollectionDelayMinutes = -10,
                },
            },
        };

        Assert.True(FcPreferencesMigration.Normalize(preferences));

        Assert.Equal(FuelStockMode.Automatic, preferences.FuelStockMode);
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
        var version = 12;
        var settings = EtaSettings.CreateDefault();
        var preferences = new FcPreferences
        {
            FuelStockMode = (FuelStockMode)999,
            FuelHolderCharacterId = 0,
            ManualCeruleumTanks = null,
            CeruleumReserve = -1,
            Submarines = new Dictionary<long, SubmarinePreferences>
            {
                [1001] = new()
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
        Assert.Equal(13, version);
        Assert.Equal(FuelStockMode.Automatic, preferences.FuelStockMode);
        Assert.Null(preferences.FuelHolderCharacterId);
        Assert.Equal(0, preferences.ManualCeruleumTanks);
        Assert.Equal(0, preferences.CeruleumReserve);
        Assert.Equal([4u, 2u], preferences.Submarines[1001].PinnedFarmingRoute);
    }

    [Fact]
    public void NewFarmingPropertiesSerializeAndDeserialize()
    {
        var preferences = new FcPreferences
        {
            FuelStockMode = FuelStockMode.Character,
            FuelHolderCharacterId = 76561198000000001,
            ManualCeruleumTanks = 450,
            CeruleumReserve = 90,
            Submarines = new Dictionary<long, SubmarinePreferences>
            {
                [1001] = new()
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
        Assert.Equal(FuelStockMode.Character, restored.FuelStockMode);
        Assert.Equal(preferences.FuelHolderCharacterId, restored.FuelHolderCharacterId);
        Assert.Equal(450, restored.ManualCeruleumTanks);
        Assert.Equal(90, restored.CeruleumReserve);
        var submarine = Assert.Single(restored.Submarines).Value;
        Assert.Equal(SubmarineAssignment.Farming, submarine.Assignment);
        Assert.Equal([7u, 12u, 18u], submarine.PinnedFarmingRoute);
        Assert.Equal(25, submarine.CollectionDelayMinutes);
    }

    [Fact]
    public void FuelOnlyPreferencesDoNotCreateSimulationOverride()
    {
        var preferences = new FcPreferences
        {
            FuelStockMode = FuelStockMode.Manual,
            ManualCeruleumTanks = 500,
            FuelHolderCharacterId = 123,
            CeruleumReserve = 100,
        };

        Assert.Null(FcSimulationOverride.FromPreferences(preferences));
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
            FuelStockMode = FuelStockMode.Character,
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
            FuelStockMode = FuelStockMode.Automatic,
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
            FuelStockMode = FuelStockMode.Manual,
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
