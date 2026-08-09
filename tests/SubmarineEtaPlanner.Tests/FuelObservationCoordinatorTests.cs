using SubmarineEtaPlanner.Fuel;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class FuelObservationCoordinatorTests
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TimestampRefreshInterval = TimeSpan.FromMinutes(10);

    [Fact]
    public void SwitchingCharactersPreservesTheOtherCharactersObservation()
    {
        var reader = new FakeReader(Observation(1, 10, 100, Utc(0)));
        var store = new RecordingStore();
        using var coordinator = CreateCoordinator(reader, store);

        coordinator.Tick(Utc(0));
        reader.Current = Observation(2, 20, 200, Utc(1));
        coordinator.Tick(Utc(1));

        Assert.Equal(2, coordinator.Observations.Count);
        Assert.Equal([1UL, 2UL], coordinator.Observations.Select(item => item.CharacterId));
        Assert.False(coordinator.Observations.Single(item => item.CharacterId == 1).IsLive);
        Assert.True(coordinator.Observations.Single(item => item.CharacterId == 2).IsLive);
        Assert.Equal(2, store.Saves.Count);
    }

    [Fact]
    public void TankCountChangeReplacesCharacterAndSavesImmediately()
    {
        var reader = new FakeReader(Observation(1, 10, 100, Utc(0)));
        var store = new RecordingStore();
        using var coordinator = CreateCoordinator(reader, store);
        coordinator.Tick(Utc(0));

        reader.Current = Observation(1, 10, 125, Utc(1));
        coordinator.Tick(Utc(1));

        Assert.Equal(125, Assert.Single(coordinator.Observations).CeruleumTanks);
        Assert.Equal(2, store.Saves.Count);
        Assert.Equal(125, Assert.Single(store.Saves[1]).CeruleumTanks);
    }

    [Fact]
    public void FreeCompanyChangeReplacesOldAssociationRatherThanDuplicatingCharacter()
    {
        var reader = new FakeReader(Observation(1, 10, 100, Utc(0)));
        var store = new RecordingStore();
        using var coordinator = CreateCoordinator(reader, store);
        coordinator.Tick(Utc(0));

        reader.Current = Observation(1, 99, 100, Utc(1));
        coordinator.Tick(Utc(1));

        var current = Assert.Single(coordinator.Observations);
        Assert.Equal(99UL, current.FreeCompanyId);
        Assert.DoesNotContain(coordinator.Observations, item => item.FreeCompanyId == 10);
        Assert.Equal(2, store.Saves.Count);
    }

    [Fact]
    public void PollingIntervalThrottlesReadsAndMaterialChangeSaves()
    {
        var reader = new FakeReader(Observation(1, 10, 100, Utc(0)));
        var store = new RecordingStore();
        using var coordinator = CreateCoordinator(reader, store);
        coordinator.Tick(Utc(0));
        reader.Current = Observation(1, 10, 200, Utc(0));

        coordinator.Tick(Utc(0).AddMilliseconds(999));

        Assert.Equal(1, reader.ReadCount);
        Assert.Single(store.Saves);
        Assert.Equal(100, Assert.Single(coordinator.Observations).CeruleumTanks);
    }

    [Fact]
    public void UnchangedObservationDoesNotSaveAgainBeforeTimestampRefresh()
    {
        var reader = new FakeReader(Observation(1, 10, 100, Utc(0)));
        var store = new RecordingStore();
        using var coordinator = CreateCoordinator(reader, store);
        coordinator.Tick(Utc(0));

        coordinator.Tick(Utc(1));

        Assert.Equal(2, reader.ReadCount);
        Assert.Single(store.Saves);
        Assert.Equal(Utc(1), coordinator.LiveObservation?.ObservedAtUtc);
    }

    [Fact]
    public void UnchangedObservationRefreshesPersistedTimestampPeriodically()
    {
        var reader = new FakeReader(Observation(1, 10, 100, Utc(0)));
        var store = new RecordingStore();
        using var coordinator = CreateCoordinator(reader, store);
        coordinator.Tick(Utc(0));

        coordinator.Tick(Utc(10));

        Assert.Equal(2, store.Saves.Count);
        Assert.Equal(Utc(10), Assert.Single(store.Saves[1]).ObservedAtUtc);
    }

    [Fact]
    public void CurrentLiveObservationSupersedesItsStoredCopy()
    {
        var stored = Observation(1, 10, 80, Utc(0)) with { IsLive = false };
        var reader = new FakeReader(Observation(1, 10, 100, Utc(5)));
        var store = new RecordingStore([stored]);
        using var coordinator = CreateCoordinator(reader, store);

        coordinator.Tick(Utc(5));

        var observation = Assert.Single(coordinator.Observations);
        Assert.Same(observation, coordinator.LiveObservation);
        Assert.True(observation.IsLive);
        Assert.Equal(100, observation.CeruleumTanks);
        Assert.Equal(Utc(5), observation.ObservedAtUtc);
    }

    [Fact]
    public void LoadedDuplicateCharactersAreNormalizedBeforeLiveUpdates()
    {
        CharacterFuelObservation[] loaded =
        [
            Observation(1, 10, 80, Utc(0)),
            Observation(1, 20, 90, Utc(1)),
        ];
        var store = new RecordingStore(loaded);
        using var coordinator = CreateCoordinator(new FakeReader(null), store);

        var normalized = Assert.Single(coordinator.Observations);
        Assert.Equal(20UL, normalized.FreeCompanyId);
        Assert.Equal(90, normalized.CeruleumTanks);
        Assert.False(normalized.IsLive);
    }

    [Fact]
    public void DisposeFlushesTheLatestUnchangedTimestampPendingInMemory()
    {
        var reader = new FakeReader(Observation(1, 10, 100, Utc(0)));
        var store = new RecordingStore();
        var coordinator = CreateCoordinator(reader, store);
        coordinator.Tick(Utc(0));
        coordinator.Tick(Utc(1));

        coordinator.Dispose();

        Assert.Equal(2, store.Saves.Count);
        Assert.Equal(Utc(1), Assert.Single(store.Saves[1]).ObservedAtUtc);
    }

    private static FuelObservationCoordinator CreateCoordinator(
        ICurrentCharacterFuelReader reader,
        IFuelObservationStore store) =>
        new(reader, store, [], PollingInterval, TimestampRefreshInterval);

    private static CharacterFuelObservation Observation(
        ulong characterId,
        ulong freeCompanyId,
        int tanks,
        DateTimeOffset observedAt) =>
        new(characterId, freeCompanyId, $"Character {characterId}", "Cerberus", tanks, observedAt, IsLive: true);

    private static DateTimeOffset Utc(int minute) =>
        new DateTimeOffset(2026, 8, 9, 14, 0, 0, TimeSpan.Zero).AddMinutes(minute);

    private sealed class FakeReader(CharacterFuelObservation? current) : ICurrentCharacterFuelReader
    {
        public CharacterFuelObservation? Current { get; set; } = current;

        public int ReadCount { get; private set; }

        public CharacterFuelObservation? TryRead(
            DateTimeOffset now,
            ICollection<string> warnings)
        {
            ReadCount++;
            return Current;
        }
    }

    private sealed class RecordingStore(IEnumerable<CharacterFuelObservation>? loaded = null) : IFuelObservationStore
    {
        private readonly CharacterFuelObservation[] loaded = loaded?.ToArray() ?? [];

        public List<CharacterFuelObservation[]> Saves { get; } = [];

        public IReadOnlyList<CharacterFuelObservation> Load(ICollection<string> warnings) => this.loaded;

        public void Save(
            IReadOnlyList<CharacterFuelObservation> observations,
            ICollection<string> warnings) =>
            Saves.Add(observations.ToArray());
    }
}
