using System.Text.Json;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class FcSetupDraftTests
{
    [Fact]
    public void RouteParserPreservesOrderAndRemovesLaterDuplicates()
    {
        var result = PinnedFarmingRouteParser.Parse("8, 3 8\t5,3", id => id is 3 or 5 or 8);

        Assert.True(result.IsValid);
        Assert.Equal([8u, 3u, 5u], result.SectorIds);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("0", "'0' is not a positive sector ID.")]
    [InlineData("-2", "'-2' is not a positive sector ID.")]
    [InlineData("one", "'one' is not a positive sector ID.")]
    [InlineData("4294967296", "'4294967296' is not a positive sector ID.")]
    public void RouteParserRejectsInvalidIds(string input, string expectedError)
    {
        var result = PinnedFarmingRouteParser.Parse(input, _ => true);

        Assert.False(result.IsValid);
        Assert.Contains(expectedError, result.Errors);
    }

    [Fact]
    public void RouteParserReportsUnknownIdsWithoutDeletingThem()
    {
        var result = PinnedFarmingRouteParser.Parse("2, 99, 3, 99", id => id is 2 or 3);

        Assert.False(result.IsValid);
        Assert.Equal([2u, 99u, 3u], result.SectorIds);
        Assert.Contains("Unknown sector IDs: 99.", result.Errors);
    }

    [Fact]
    public void RouteParserRejectsEmptyInputWithClearPinGuidance()
    {
        var result = PinnedFarmingRouteParser.Parse("  ,  ", _ => true);

        Assert.False(result.IsValid);
        Assert.Contains("Clear pin", result.ErrorMessage);
    }

    [Fact]
    public void CapturedRoleDraftSerializesAndRoundTrips()
    {
        var preferences = new FcPreferences
        {
            TargetRankOverride = 120,
            FuelStockMode = FuelStockMode.Character,
            FuelHolderCharacterId = 123,
            ManualCeruleumTanks = 450,
            CeruleumReserve = 90,
            Submarines = new Dictionary<long, SubmarinePreferences>
            {
                [10] = new()
                {
                    Assignment = SubmarineAssignment.Farming,
                    PinnedFarmingRoute = [8, 3, 5],
                },
            },
        };
        var draft = FcSetupDraft.Capture(preferences, [10, 20]);

        var json = JsonSerializer.Serialize(draft);
        var restored = JsonSerializer.Deserialize<FcSetupDraft>(json);

        Assert.NotNull(restored);
        Assert.Equal(120, restored.TargetRankOverride);
        Assert.Equal(FuelStockMode.Character, restored.FuelStockMode);
        Assert.Equal(123UL, restored.FuelHolderCharacterId);
        Assert.Equal(450, restored.ManualCeruleumTanks);
        Assert.Equal(90, restored.CeruleumReserve);
        Assert.Equal(SubmarineAssignment.Farming, restored.Submarines[10].Assignment);
        Assert.Equal([8u, 3u, 5u], restored.Submarines[10].PinnedFarmingRoute);
        Assert.Equal(SubmarineAssignment.Auto, restored.Submarines[20].Assignment);
        Assert.Null(restored.Submarines[20].PinnedFarmingRoute);
    }

    [Fact]
    public void ApplySavesAssignmentsAndPinsWithoutGeneratingAutomaticPreferenceClutter()
    {
        var preferences = new FcPreferences();
        var draft = FcSetupDraft.Capture(preferences, [10, 20])
            .WithSubmarine(10, new SubmarineSetupDraft(SubmarineAssignment.Leveling, [4, 2]))
            .WithSubmarine(20, SubmarineSetupDraft.Automatic);

        var result = draft.ApplyTo(preferences);

        Assert.True(result.AssignmentChanged);
        Assert.True(result.PinnedRouteChanged);
        Assert.True(result.EtaRefreshRequired);
        var saved = Assert.Single(preferences.Submarines);
        Assert.Equal(10, saved.Key);
        Assert.Equal(SubmarineAssignment.Leveling, saved.Value.Assignment);
        Assert.Equal([4u, 2u], saved.Value.PinnedFarmingRoute);
    }

    [Fact]
    public void RevertedDraftAppliesNoChanges()
    {
        var preferences = new FcPreferences
        {
            Submarines = new Dictionary<long, SubmarinePreferences>
            {
                [10] = new()
                {
                    Assignment = SubmarineAssignment.Farming,
                    PinnedFarmingRoute = [4, 2],
                },
            },
        };
        var original = FcSetupDraft.Capture(preferences, [10]);
        var edited = original.WithSubmarine(10, new SubmarineSetupDraft(SubmarineAssignment.Paused, [7]));

        var reverted = FcSetupDraft.Capture(preferences, edited.Submarines.Keys);
        var result = reverted.ApplyTo(preferences);

        Assert.False(result.FcSettingsChanged);
        Assert.False(result.FuelSettingsChanged);
        Assert.False(result.AssignmentChanged);
        Assert.False(result.PinnedRouteChanged);
        Assert.False(result.EtaRefreshRequired);
        Assert.Equal(SubmarineAssignment.Farming, preferences.Submarines[10].Assignment);
        Assert.Equal([4u, 2u], preferences.Submarines[10].PinnedFarmingRoute);
    }

    [Fact]
    public void FuelChangesStayInDraftAndSaveWithoutEtaRefresh()
    {
        var preferences = new FcPreferences
        {
            FuelStockMode = FuelStockMode.Automatic,
            FuelHolderCharacterId = 10,
            ManualCeruleumTanks = 2_400,
            CeruleumReserve = null,
        };
        var captured = FcSetupDraft.Capture(preferences, []);
        var edited = captured with
        {
            FuelStockMode = FuelStockMode.Manual,
            FuelHolderCharacterId = 20,
            ManualCeruleumTanks = 1_800,
            CeruleumReserve = 500,
        };

        Assert.Equal(FuelStockMode.Automatic, preferences.FuelStockMode);
        Assert.Null(preferences.CeruleumReserve);

        var result = edited.ApplyTo(preferences);

        Assert.True(result.FuelSettingsChanged);
        Assert.False(result.FcSettingsChanged);
        Assert.False(result.AssignmentChanged);
        Assert.False(result.EtaRefreshRequired);
        Assert.Equal(FuelStockMode.Manual, preferences.FuelStockMode);
        Assert.Equal(20UL, preferences.FuelHolderCharacterId);
        Assert.Equal(1_800, preferences.ManualCeruleumTanks);
        Assert.Equal(500, preferences.CeruleumReserve);
    }

    [Fact]
    public void RevertRestoresFuelDraftIncludingInactiveManualValue()
    {
        var preferences = new FcPreferences
        {
            FuelStockMode = FuelStockMode.Character,
            FuelHolderCharacterId = 10,
            ManualCeruleumTanks = 2_400,
            CeruleumReserve = null,
        };
        var edited = FcSetupDraft.Capture(preferences, []) with
        {
            FuelStockMode = FuelStockMode.Manual,
            ManualCeruleumTanks = 1,
            CeruleumReserve = 500,
        };

        var reverted = FcSetupDraft.Capture(preferences, edited.Submarines.Keys);

        Assert.Equal(FuelStockMode.Character, reverted.FuelStockMode);
        Assert.Equal(10UL, reverted.FuelHolderCharacterId);
        Assert.Equal(2_400, reverted.ManualCeruleumTanks);
        Assert.Null(reverted.CeruleumReserve);
    }

    [Fact]
    public void PinnedRouteOnlySaveDoesNotRequireEtaRefresh()
    {
        var preferences = new FcPreferences
        {
            Submarines = new Dictionary<long, SubmarinePreferences>
            {
                [10] = new()
                {
                    Assignment = SubmarineAssignment.Farming,
                    PinnedFarmingRoute = [4, 2],
                },
            },
        };
        var draft = FcSetupDraft.Capture(preferences, [10])
            .WithSubmarine(10, new SubmarineSetupDraft(SubmarineAssignment.Farming, [7, 8]));

        var result = draft.ApplyTo(preferences);

        Assert.False(result.FcSettingsChanged);
        Assert.False(result.AssignmentChanged);
        Assert.True(result.PinnedRouteChanged);
        Assert.False(result.EtaRefreshRequired);
    }

    [Fact]
    public void AssignmentSavedByDraftIsIncludedInSimulationRequest()
    {
        var preferences = new FcPreferences();
        var draft = FcSetupDraft.Capture(preferences, [10])
            .WithSubmarine(10, new SubmarineSetupDraft(SubmarineAssignment.Paused, null));

        draft.ApplyTo(preferences);
        var simulationOverride = Planner.FcSimulationOverride.FromPreferences(preferences);
        var request = new Planner.PlannerCalculationRequest(
            Planner.EtaSettings.CreateDefault(),
            new Dictionary<string, Planner.FcSimulationOverride>
            {
                ["FC"] = simulationOverride!,
            });

        Assert.NotNull(simulationOverride);
        Assert.Equal(
            SubmarineAssignment.Paused,
            request.FreeCompanyOverrides["FC"].SubmarineAssignments[10]);
    }

    [Fact]
    public void ApplyPreservesSavedPreferencesOutsideTheDisplayedFleet()
    {
        var preferences = new FcPreferences
        {
            Submarines = new Dictionary<long, SubmarinePreferences>
            {
                [99] = new()
                {
                    Assignment = SubmarineAssignment.Farming,
                    PinnedFarmingRoute = [7, 8],
                },
            },
        };
        var displayedFleetDraft = FcSetupDraft.Capture(preferences, [10]);

        displayedFleetDraft.ApplyTo(preferences);

        var preserved = Assert.Single(preferences.Submarines);
        Assert.Equal(99, preserved.Key);
        Assert.Equal(SubmarineAssignment.Farming, preserved.Value.Assignment);
        Assert.Equal([7u, 8u], preserved.Value.PinnedFarmingRoute);
    }
}
