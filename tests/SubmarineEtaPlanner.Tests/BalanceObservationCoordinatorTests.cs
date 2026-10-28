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

    [Fact]
    public void LastReadKeepsUnknownRawAssociationSeparateFromTheRecordedMembership()
    {
        var reader = new FakeReader { Current = Read(1, 10, 100) };
        using var coordinator = new BalanceObservationCoordinator(reader, new FakeStore(), []);
        coordinator.Tick(Now);
        var diagnostics = new BalanceAssociationDiagnostics(67, 67, 67, 67, true, 0, 67, false, null);
        reader.Current = new(Read(1, null, 120).Character, null, diagnostics);

        coordinator.Tick(Now.AddSeconds(1));

        Assert.NotNull(coordinator.LastRead);
        Assert.Null(coordinator.LastRead.Character.FreeCompanyId);
        Assert.Equal(diagnostics, coordinator.LastRead.Association);
        Assert.Equal(10UL, Assert.Single(coordinator.Snapshot.Characters).FreeCompanyId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0UL)]
    public void MissingFcNameDoesNotPreventRepairingAndSavingAnUnassignedWalletWithUnchangedGil(
        ulong? previouslyRecordedFreeCompanyId)
    {
        var character = new CharacterGilObservation(1, previouslyRecordedFreeCompanyId,
            "Character", "World", 100, Now, false);
        var store = new FakeStore { Loaded = new([character], []) };
        var reader = new FakeReader { Current = new(character with { IsLive = true }, null) };
        using var coordinator = new BalanceObservationCoordinator(reader, store, []);
        coordinator.Tick(Now);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(previouslyRecordedFreeCompanyId,
            Assert.Single(coordinator.Snapshot.Characters).FreeCompanyId);
        var diagnostics = new BalanceAssociationDiagnostics(1, 1, 1, 1, true, 10, 1, false, 10);
        var (freeCompanyId, _) = diagnostics.Resolve();
        reader.Current = new(character with { FreeCompanyId = freeCompanyId, IsLive = true },
            null, diagnostics);

        coordinator.Tick(Now.AddSeconds(1));

        Assert.Equal(10UL, Assert.Single(coordinator.Snapshot.Characters).FreeCompanyId);
        Assert.Equal(100, GilBalanceCalculator.ForFreeCompany(10, coordinator.Snapshot).WalletGil);
        Assert.Empty(GilBalanceCalculator.UnassignedCharacters(coordinator.Snapshot));
        Assert.Equal(2, store.SaveCount);
        Assert.Equal(10UL, Assert.Single(store.Saved!.Characters).FreeCompanyId);
        Assert.Equal(100, Assert.Single(store.Saved.Characters).Gil);
    }

    [Fact]
    public void AReadWithoutDiagnosticsReplacesThePreviousDiagnosticSample()
    {
        var diagnostics = new BalanceAssociationDiagnostics(67, 67, 67, 67, true, 10, 67, true, 10);
        var reader = new FakeReader { Current = new(Read(1, 10, 100).Character, null, diagnostics) };
        using var coordinator = new BalanceObservationCoordinator(reader, new FakeStore(), []);
        coordinator.Tick(Now);
        reader.Current = Read(2, 20, 200);

        coordinator.Tick(Now.AddSeconds(1));

        Assert.NotNull(coordinator.LastRead);
        Assert.Equal(2UL, coordinator.LastRead.Character.CharacterId);
        Assert.Null(coordinator.LastRead.Association);
    }

    [Theory]
    [InlineData(0UL, 100L)]
    [InlineData(1UL, -1L)]
    [InlineData(1UL, 4294967296L)]
    public void InvalidReadClearsThePreviousDiagnosticSample(ulong characterId, long gil)
    {
        var reader = new FakeReader { Current = Read(1, 10, 100) };
        using var coordinator = new BalanceObservationCoordinator(reader, new FakeStore(), []);
        coordinator.Tick(Now);
        Assert.NotNull(coordinator.LastRead);
        reader.Current = Read(characterId, 10, gil);

        coordinator.Tick(Now.AddSeconds(1));

        Assert.Null(coordinator.LastRead);
        Assert.Equal(100, Assert.Single(coordinator.Snapshot.Characters).Gil);
    }

    [Fact]
    public void MissingReadOrReaderExceptionClearsThePreviousDiagnosticSample()
    {
        var reader = new FakeReader { Current = Read(1, 10, 100) };
        using var coordinator = new BalanceObservationCoordinator(reader, new FakeStore(), []);
        coordinator.Tick(Now);
        Assert.NotNull(coordinator.LastRead);
        reader.Current = null;
        coordinator.Tick(Now.AddSeconds(1));
        Assert.Null(coordinator.LastRead);

        reader.Current = Read(2, 20, 200);
        coordinator.Tick(Now.AddSeconds(2));
        Assert.NotNull(coordinator.LastRead);
        reader.ThrowOnRead = true;
        coordinator.Tick(Now.AddSeconds(3));

        Assert.Null(coordinator.LastRead);
        Assert.Equal(2, coordinator.Snapshot.Characters.Count);
        Assert.All(coordinator.Snapshot.Characters, character => Assert.False(character.IsLive));
    }

    private static BalanceReadResult Read(ulong id, ulong? fc, long gil, FcChestGilObservation? chest = null)
        => new(new(id, fc, $"Character {id}", "Cerberus", gil, Now, true), chest);

    private sealed class FakeReader : ICurrentBalanceReader
    {
        public BalanceReadResult? Current { get; set; }
        public bool ThrowOnRead { get; set; }
        public int ReadCount { get; private set; }
        public BalanceReadResult? TryRead(DateTimeOffset now, ICollection<string> warnings)
        {
            ReadCount++;
            if (ThrowOnRead) throw new InvalidOperationException("Simulated reader failure.");
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
