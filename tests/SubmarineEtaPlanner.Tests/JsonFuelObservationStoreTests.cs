using System.Text;
using System.Text.Json;
using SubmarineEtaPlanner.Fuel;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class JsonFuelObservationStoreTests
{
    [Fact]
    public void MissingFileLoadsAsEmptyWithoutWarning()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.FilePath);
        var warnings = new List<string>();

        var observations = store.Load(warnings);

        Assert.Empty(observations);
        Assert.Empty(warnings);
    }

    [Fact]
    public void EmptyObservationFileLoadsAsEmptyWithoutWarning()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(directory.FilePath, "{\"Version\":1,\"Characters\":[]}");
        var warnings = new List<string>();

        var observations = CreateStore(directory.FilePath).Load(warnings);

        Assert.Empty(observations);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ObservationRoundTripsAsStoredUtcSnapshot()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.FilePath);
        var observedAt = new DateTimeOffset(2026, 8, 9, 14, 30, 0, TimeSpan.FromHours(2));
        var observation = Observation(123, 456, 789, observedAt, isLive: true);

        store.Save([observation], []);
        var loaded = Assert.Single(store.Load([]));

        Assert.Equal(observation.CharacterId, loaded.CharacterId);
        Assert.Equal(observation.FreeCompanyId, loaded.FreeCompanyId);
        Assert.Equal(observation.CharacterName, loaded.CharacterName);
        Assert.Equal(observation.World, loaded.World);
        Assert.Equal(observation.CeruleumTanks, loaded.CeruleumTanks);
        Assert.Equal(observedAt.ToUniversalTime(), loaded.ObservedAtUtc);
        Assert.False(loaded.IsLive);
        Assert.NotEqual(default, loaded.ObservedAtUtc);
    }

    [Fact]
    public void TwoCharactersRoundTripIndependently()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.FilePath);
        CharacterFuelObservation[] observations =
        [
            Observation(222, 20, 2, Utc(2)),
            Observation(111, 10, 1, Utc(1)),
        ];

        store.Save(observations, []);
        var loaded = store.Load([]);

        Assert.Equal([111UL, 222UL], loaded.Select(item => item.CharacterId));
        Assert.Equal([1, 2], loaded.Select(item => item.CeruleumTanks));
    }

    [Fact]
    public void DuplicateCharacterIdsNormalizeToNewestObservation()
    {
        using var directory = new TemporaryDirectory();
        var stored = new StoredFuelObservationFile
        {
            Characters =
            [
                StoredObservation(123, 100, 1, Utc(1)),
                StoredObservation(123, 200, 2, Utc(2)),
                StoredObservation(456, 300, 3, Utc(3)),
            ],
        };
        File.WriteAllText(directory.FilePath, JsonSerializer.Serialize(stored));

        var loaded = CreateStore(directory.FilePath).Load([]);

        Assert.Equal(2, loaded.Count);
        var normalized = Assert.Single(loaded, item => item.CharacterId == 123);
        Assert.Equal(200UL, normalized.FreeCompanyId);
        Assert.Equal(2, normalized.CeruleumTanks);
    }

    [Fact]
    public void MalformedJsonIsPreservedAndDoesNotThrow()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(directory.FilePath, "{ definitely not json");
        var warnings = new List<string>();
        var now = new DateTimeOffset(2026, 8, 9, 14, 25, 30, TimeSpan.Zero);
        var store = new JsonFuelObservationStore(
            directory.FilePath,
            () => now,
            new AtomicFileWriter());

        var loaded = store.Load(warnings);

        Assert.Empty(loaded);
        Assert.Contains(warnings, warning => warning.Contains("could not be loaded", StringComparison.Ordinal));
        Assert.False(File.Exists(directory.FilePath));
        var corruptPath = Path.Combine(directory.Path, "workshop-fuel-observations.corrupt-20260809-142530.json");
        Assert.Equal("{ definitely not json", File.ReadAllText(corruptPath));
    }

    [Fact]
    public void UnsupportedFutureVersionIsNotLoadedOrOverwritten()
    {
        using var directory = new TemporaryDirectory();
        var original = "{\"Version\":999,\"Characters\":[]}";
        File.WriteAllText(directory.FilePath, original);
        var warnings = new List<string>();
        var store = CreateStore(directory.FilePath);

        Assert.Empty(store.Load(warnings));
        store.Save([Observation(1, 2, 3, Utc(1))], warnings);

        Assert.Equal(original, File.ReadAllText(directory.FilePath));
        Assert.Contains(warnings, warning => warning.Contains("future version 999", StringComparison.Ordinal));
        Assert.Contains(warnings, warning => warning.Contains("unsupported version 999", StringComparison.Ordinal));
    }

    [Fact]
    public void AtomicWriterFallsBackToOverwriteMoveWhenReplaceIsUnavailable()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(directory.FilePath, "previous");
        var replaceAttempts = 0;
        var writer = new AtomicFileWriter(
            File.Exists,
            (_, _, _) =>
            {
                replaceAttempts++;
                throw new PlatformNotSupportedException();
            },
            File.Move);

        writer.Write(
            directory.FilePath,
            stream => stream.Write(Encoding.UTF8.GetBytes("replacement")));

        Assert.Equal(1, replaceAttempts);
        Assert.Equal("replacement", File.ReadAllText(directory.FilePath));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public void FailedAtomicWriteLeavesPreviousValidFileUntouched()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.FilePath);
        store.Save([Observation(1, 2, 3, Utc(1))], []);
        var original = File.ReadAllText(directory.FilePath);
        var warnings = new List<string>();
        var failingStore = new JsonFuelObservationStore(
            directory.FilePath,
            () => Utc(5),
            new FailingAtomicFileWriter());

        failingStore.Save([Observation(1, 2, 999, Utc(2))], warnings);

        Assert.Equal(original, File.ReadAllText(directory.FilePath));
        Assert.Contains(warnings, warning => warning.Contains("previous file was preserved", StringComparison.Ordinal));
    }

    private static JsonFuelObservationStore CreateStore(string filePath) =>
        new(filePath, () => Utc(10), new AtomicFileWriter());

    private static CharacterFuelObservation Observation(
        ulong characterId,
        ulong freeCompanyId,
        int tanks,
        DateTimeOffset observedAt,
        bool isLive = false) =>
        new(characterId, freeCompanyId, $"Character {characterId}", "Cerberus", tanks, observedAt, isLive);

    private static StoredCharacterFuelObservation StoredObservation(
        ulong characterId,
        ulong freeCompanyId,
        int tanks,
        DateTimeOffset observedAt) =>
        new()
        {
            CharacterId = characterId,
            FreeCompanyId = freeCompanyId,
            CharacterName = $"Character {characterId}",
            World = "Cerberus",
            CeruleumTanks = tanks,
            ObservedAtUtc = observedAt,
        };

    private static DateTimeOffset Utc(int minute) =>
        new(2026, 8, 9, 14, minute, 0, TimeSpan.Zero);

    private sealed class FailingAtomicFileWriter : IAtomicFileWriter
    {
        public void Write(string destinationPath, Action<Stream> writeContent) =>
            throw new IOException("Simulated disk failure");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"seta-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
            FilePath = System.IO.Path.Combine(Path, "workshop-fuel-observations.json");
        }

        public string Path { get; }

        public string FilePath { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
