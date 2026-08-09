using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class FuelStockResolverTests
{
    private static readonly DateTimeOffset OlderObservationTime =
        new(2026, 8, 8, 10, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset NewerObservationTime =
        new(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ManualModeUsesEnteredCountAsCurrentManualStock()
    {
        var result = FuelStockResolver.Resolve(100, FuelStockMode.Manual, 1, 42, []);

        Assert.True(result.IsAvailable);
        Assert.Equal(42, result.CeruleumTanks);
        Assert.Equal(FuelStockSourceKind.Manual, result.Source);
        Assert.Null(result.CharacterId);
        Assert.Null(result.CharacterName);
        Assert.Null(result.World);
        Assert.Null(result.ObservedAtUtc);
        Assert.True(result.IsLive);
        Assert.Null(result.UnavailableReason);
    }

    [Fact]
    public void CharacterModeUsesOnlySelectedCharacterInRequestedFc()
    {
        var selected = Observation(1, 100, 25, isLive: true);
        var result = FuelStockResolver.Resolve(
            100,
            FuelStockMode.Character,
            selectedCharacterId: 1,
            manualCeruleumTanks: 999,
            [Observation(2, 100, 700), selected]);

        Assert.True(result.IsAvailable);
        Assert.Equal(25, result.CeruleumTanks);
        Assert.Equal(FuelStockSourceKind.LiveCharacter, result.Source);
        Assert.Equal(1ul, result.CharacterId);
        Assert.Equal("Character 1", result.CharacterName);
        Assert.Equal("World 1", result.World);
        Assert.Equal(NewerObservationTime, result.ObservedAtUtc);
        Assert.True(result.IsLive);
    }

    [Fact]
    public void AutomaticModeUsesOnlyStoredCandidate()
    {
        var result = FuelStockResolver.Resolve(
            100,
            FuelStockMode.Automatic,
            selectedCharacterId: null,
            manualCeruleumTanks: 0,
            [Observation(1, 100, 36)]);

        Assert.True(result.IsAvailable);
        Assert.Equal(36, result.CeruleumTanks);
        Assert.Equal(FuelStockSourceKind.LastObservedCharacter, result.Source);
        Assert.Equal(1ul, result.CharacterId);
        Assert.False(result.IsLive);
    }

    [Fact]
    public void AutomaticModePrefersLiveCandidateOverStoredCandidate()
    {
        var result = FuelStockResolver.Resolve(
            100,
            FuelStockMode.Automatic,
            selectedCharacterId: null,
            manualCeruleumTanks: 0,
            [Observation(1, 100, 70), Observation(2, 100, 15, isLive: true)]);

        Assert.True(result.IsAvailable);
        Assert.Equal(15, result.CeruleumTanks);
        Assert.Equal(FuelStockSourceKind.LiveCharacter, result.Source);
        Assert.Equal(2ul, result.CharacterId);
    }

    [Fact]
    public void AutomaticModeDoesNotSumOrChooseBetweenMultipleStoredCharacters()
    {
        var result = FuelStockResolver.Resolve(
            100,
            FuelStockMode.Automatic,
            selectedCharacterId: null,
            manualCeruleumTanks: 0,
            [Observation(1, 100, 20), Observation(2, 100, 30)]);

        Assert.False(result.IsAvailable);
        Assert.Null(result.CeruleumTanks);
        Assert.Null(result.Source);
        Assert.Equal(
            "Multiple characters have been observed in this FC. Choose the character that carries the workshop fuel.",
            result.UnavailableReason);
    }

    [Fact]
    public void CharacterModeRejectsSelectedCharacterObservedInAnotherFc()
    {
        var result = FuelStockResolver.Resolve(
            100,
            FuelStockMode.Character,
            selectedCharacterId: 1,
            manualCeruleumTanks: 0,
            [Observation(1, 200, 25, isLive: true)]);

        Assert.False(result.IsAvailable);
        Assert.Null(result.CeruleumTanks);
        Assert.Equal(
            "The selected fuel-holder character is no longer associated with this FC.",
            result.UnavailableReason);
    }

    [Fact]
    public void CharacterModeWithoutASelectionRequestsAHolder()
    {
        var result = FuelStockResolver.Resolve(
            100,
            FuelStockMode.Character,
            selectedCharacterId: null,
            manualCeruleumTanks: 0,
            [Observation(1, 100, 25)]);

        Assert.False(result.IsAvailable);
        Assert.Equal("Choose the character that carries the workshop fuel.", result.UnavailableReason);
    }

    [Fact]
    public void MissingNumericFreeCompanyIdBlocksObservedSourcesButNotManualSource()
    {
        var automatic = FuelStockResolver.Resolve(
            null,
            FuelStockMode.Automatic,
            null,
            0,
            [Observation(1, 100, 25, isLive: true)]);
        var manual = FuelStockResolver.Resolve(null, FuelStockMode.Manual, null, 25, []);

        Assert.False(automatic.IsAvailable);
        Assert.Contains("numeric FC ID could not be decoded", automatic.UnavailableReason);
        Assert.True(manual.IsAvailable);
        Assert.Equal(25, manual.CeruleumTanks);
        Assert.Equal(FuelStockSourceKind.Manual, manual.Source);
    }

    [Fact]
    public void NegativeManualCountIsClampedToZero()
    {
        var result = FuelStockResolver.Resolve(null, FuelStockMode.Manual, null, -5, []);

        Assert.True(result.IsAvailable);
        Assert.Equal(0, result.CeruleumTanks);
    }

    [Fact]
    public void LiveObservationReplacesStoredObservationForSameCharacter()
    {
        var result = FuelStockResolver.Resolve(
            100,
            FuelStockMode.Automatic,
            selectedCharacterId: null,
            manualCeruleumTanks: 0,
            [
                Observation(1, 100, 80, observedAtUtc: NewerObservationTime),
                Observation(1, 100, 12, isLive: true, observedAtUtc: OlderObservationTime),
            ]);

        Assert.True(result.IsAvailable);
        Assert.Equal(12, result.CeruleumTanks);
        Assert.Equal(FuelStockSourceKind.LiveCharacter, result.Source);
        Assert.Equal(1ul, result.CharacterId);
    }

    [Fact]
    public void AutomaticModeWithoutObservationsIsUnavailableWithReason()
    {
        var result = FuelStockResolver.Resolve(100, FuelStockMode.Automatic, null, 0, []);

        Assert.False(result.IsAvailable);
        Assert.Null(result.CeruleumTanks);
        Assert.Null(result.Source);
        Assert.False(result.IsLive);
        Assert.Equal("No character inventory has been observed for this FC.", result.UnavailableReason);
    }

    [Fact]
    public void ZeroTanksIsAvailableStock()
    {
        var result = FuelStockResolver.Resolve(
            100,
            FuelStockMode.Automatic,
            selectedCharacterId: null,
            manualCeruleumTanks: 999,
            [Observation(1, 100, 0)]);

        Assert.True(result.IsAvailable);
        Assert.Equal(0, result.CeruleumTanks);
        Assert.Null(result.UnavailableReason);
    }

    [Fact]
    public void ConfigurationMigrationNormalizesNegativeManualCountBeforeResolution()
    {
        var preferences = new FcPreferences
        {
            FuelStockMode = FuelStockMode.Manual,
            ManualCeruleumTanks = -50,
        };

        Assert.True(FcPreferencesMigration.Normalize(preferences));

        var result = FuelStockResolver.Resolve(
            100,
            preferences.FuelStockMode,
            preferences.FuelHolderCharacterId,
            preferences.ManualCeruleumTanks.GetValueOrDefault(),
            []);

        Assert.True(result.IsAvailable);
        Assert.Equal(0, result.CeruleumTanks);
    }

    [Fact]
    public void ResolutionIsDeterministicRegardlessOfInputOrdering()
    {
        CharacterFuelObservation[] observations =
        [
            Observation(1, 100, 90, observedAtUtc: OlderObservationTime),
            Observation(1, 100, 80, observedAtUtc: NewerObservationTime),
            Observation(2, 100, 17, isLive: true, observedAtUtc: OlderObservationTime),
            Observation(3, 200, 999, isLive: true),
        ];

        var forward = FuelStockResolver.Resolve(100, FuelStockMode.Automatic, null, 0, observations);
        var reverse = FuelStockResolver.Resolve(100, FuelStockMode.Automatic, null, 0, observations.Reverse().ToArray());

        Assert.Equal(forward, reverse);
        Assert.Equal(17, forward.CeruleumTanks);
        Assert.Equal(2ul, forward.CharacterId);
    }

    private static CharacterFuelObservation Observation(
        ulong characterId,
        ulong freeCompanyId,
        int ceruleumTanks,
        bool isLive = false,
        DateTimeOffset? observedAtUtc = null) =>
        new(
            characterId,
            freeCompanyId,
            $"Character {characterId}",
            $"World {characterId}",
            ceruleumTanks,
            observedAtUtc ?? NewerObservationTime,
            isLive);
}
