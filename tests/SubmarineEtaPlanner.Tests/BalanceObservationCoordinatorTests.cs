using SubmarineEtaPlanner.Balances;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class BalanceObservationCoordinatorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-26T10:00:00Z");

    [Fact]
    public void UnknownAssociationPreservesKnownFcButConfirmedLeaveUnassigns()
    {
        var reader = new FakeReader { Current = Read(1, 10, 100) };
        var store = new FakeStore();
        using var coordinator = new BalanceObservationCoordinator(reader, store, []);
        coordinator.Tick(Now);
        reader.Current = Read(1, null, 120);
        coordinator.Tick(Now.AddSeconds(1));
        Assert.Equal((ulong)10, Assert.Single(coordinator.Snapshot.Characters).FreeCompanyId);
        reader.Current = Read(1, 20, 130);
        coordinator.Tick(Now.AddSeconds(2));
        Assert.Null(GilBalanceCalculator.ForFreeCompany(10, coordinator.Snapshot).WalletGil);
        Assert.Equal(130, GilBalanceCalculator.ForFreeCompany(20, coordinator.Snapshot).WalletGil);
        reader.Current = Read(1, 0, 140);
        coordinator.Tick(Now.AddSeconds(3));
        Assert.Equal((ulong)0, Assert.Single(coordinator.Snapshot.Characters).FreeCompanyId);
        Assert.Single(GilBalanceCalculator.UnassignedCharacters(coordinator.Snapshot));
    }

    [Fact]
    public void SharedChestIsReplacedAndMismatchedChestIsRejected()
    {
        var reader = new FakeReader { Current = Read(1, 10, 100, new(10, 500, Now)) };
        using var coordinator = new BalanceObservationCoordinator(reader, new FakeStore(), []);
        coordinator.Tick(Now);
        reader.Current = Read(2, 10, 200, new(10, 600, Now));
        coordinator.Tick(Now.AddSeconds(1));
        Assert.Equal(2, coordinator.Snapshot.Characters.Count);
        Assert.Equal(600, Assert.Single(coordinator.Snapshot.Chests).Gil);
        reader.Current = Read(2, null, 200, new(20, 900, Now));
        coordinator.Tick(Now.AddSeconds(2));
        Assert.Equal((ulong)10, Assert.Single(coordinator.Snapshot.Chests).FreeCompanyId);
    }

    [Fact]
    public void PollsOncePerSecondAndCheckpointsUnchangedTimestamps()
    {
        var reader = new FakeReader { Current = Read(1, 10, 100) };
        var store = new FakeStore();
        using var coordinator = new BalanceObservationCoordinator(reader, store, []);
        coordinator.Tick(Now);
        coordinator.Tick(Now.AddMilliseconds(500));
        coordinator.Tick(Now.AddSeconds(1));
        Assert.Equal(2, reader.ReadCount);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(Now.AddSeconds(1), Assert.Single(coordinator.Snapshot.Characters).ObservedAtUtc);
        coordinator.Tick(Now.AddMinutes(10));
        Assert.Equal(2, store.SaveCount);
    }

    [Fact]
    public void LogoutPreservesSnapshotAndFailedSaveRetryRemainsThrottled()
    {
        var reader = new FakeReader { Current = Read(1, 10, 100, new(10, 50, Now)) };
        var store = new FakeStore { SaveSucceeds = false };
        using var coordinator = new BalanceObservationCoordinator(reader, store, []);
        coordinator.Tick(Now);
        reader.Current = null;
        coordinator.Tick(Now.AddSeconds(1));
        coordinator.Tick(Now.AddSeconds(29));
        Assert.Equal(1, store.SaveCount);
        Assert.False(Assert.Single(coordinator.Snapshot.Characters).IsLive);
        Assert.Equal(50, Assert.Single(coordinator.Snapshot.Chests).Gil);
        store.SaveSucceeds = true;
        coordinator.Tick(Now.AddSeconds(30));
        Assert.Equal(2, store.SaveCount);
    }

    [Fact]
    public void LoadedSnapshotsStartOfflineAndDisposalFlushesPendingTimestamps()
    {
        var reader = new FakeReader { Current = Read(1, 10, 100) };
        var store = new FakeStore { Loaded = new([Read(1, 10, 100).Character], []) };
        var coordinator = new BalanceObservationCoordinator(reader, store, []);
        Assert.False(Assert.Single(coordinator.Snapshot.Characters).IsLive);
        coordinator.Tick(Now);
        coordinator.Tick(Now.AddSeconds(1));
        coordinator.Dispose();
        Assert.Equal(2, store.SaveCount);
        Assert.Equal(Now.AddSeconds(1), Assert.Single(store.Saved!.Characters).ObservedAtUtc);
        coordinator.Dispose();
        Assert.Equal(2, store.SaveCount);
    }

    [Fact]
    public void AFailedMaterialChangeRetriesWhileStillLoggedInBeforeTheTimestampCheckpoint()
    {
        var reader = new FakeReader { Current = Read(1, 10, 100) };
        var store = new FakeStore();
        using var coordinator = new BalanceObservationCoordinator(reader, store, []);
        coordinator.Tick(Now);
        store.SaveSucceeds = false;
        reader.Current = Read(1, 10, 200);
        coordinator.Tick(Now.AddSeconds(1));
        store.SaveSucceeds = true;
        coordinator.Tick(Now.AddSeconds(30));
        Assert.Equal(2, store.SaveCount);
        coordinator.Tick(Now.AddSeconds(31));
        Assert.Equal(3, store.SaveCount);
        Assert.Equal(200, Assert.Single(store.Saved!.Characters).Gil);
    }

    private static BalanceReadResult Read(ulong id, ulong? fc, long gil, FcChestGilObservation? chest = null)
        => new(new(id, fc, $"Character {id}", "Cerberus", gil, Now, true), chest);

    private sealed class FakeReader : ICurrentBalanceReader
    {
        public BalanceReadResult? Current { get; set; }
        public int ReadCount { get; private set; }
        public BalanceReadResult? TryRead(DateTimeOffset now, ICollection<string> warnings)
        {
            ReadCount++;
            return Current;
        }
    }

    private sealed class FakeStore : IBalanceObservationStore
    {
        public GilBalanceSnapshot Loaded { get; init; } = GilBalanceSnapshot.Empty;
        public GilBalanceSnapshot? Saved { get; private set; }
        public int SaveCount { get; private set; }
        public bool SaveSucceeds { get; set; } = true;
        public GilBalanceSnapshot Load(ICollection<string> warnings) => Loaded;
        public bool Save(GilBalanceSnapshot snapshot, ICollection<string> warnings)
        {
            SaveCount++;
            Saved = snapshot;
            return SaveSucceeds;
        }
    }
}
