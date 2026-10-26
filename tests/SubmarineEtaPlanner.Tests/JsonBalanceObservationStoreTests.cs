using SubmarineEtaPlanner.Balances;
using SubmarineEtaPlanner.Fuel;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class JsonBalanceObservationStoreTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-26T10:00:00Z");

    [Fact]
    public void RoundTripKeepsSeparateBalancesAndClearsLiveState()
    {
        using var directory = new TempDirectory();
        var store = new JsonBalanceObservationStore(directory.Path);
        var snapshot = new GilBalanceSnapshot(
            [new(1, 10, "Character One", "Cerberus", 0, Now, true)],
            [new(10, 12345, Now.AddMinutes(-10))]);
        var warnings = new List<string>();
        Assert.True(store.Save(snapshot, warnings));
        var read = store.Load(warnings);
        Assert.Equal(0, Assert.Single(read.Characters).Gil);
        Assert.False(Assert.Single(read.Characters).IsLive);
        Assert.Equal(Now, read.Characters[0].ObservedAtUtc);
        Assert.Equal(12345, Assert.Single(read.Chests).Gil);
        Assert.Empty(warnings);
    }

    [Fact]
    public void FutureVersionCannotBeOverwritten()
    {
        using var directory = new TempDirectory();
        const string original = "{\"Version\":99,\"Characters\":[],\"Chests\":[]}";
        File.WriteAllText(directory.Path, original);
        var warnings = new List<string>();
        var store = new JsonBalanceObservationStore(directory.Path);
        Assert.Empty(store.Load(warnings).Characters);
        Assert.False(store.Save(GilBalanceSnapshot.Empty, warnings));
        Assert.Equal(original, File.ReadAllText(directory.Path));
        Assert.Single(warnings);
    }

    [Fact]
    public void MissingRequiredWalletAmountIsPreservedAsCorruptInsteadOfBecomingZero()
    {
        using var directory = new TempDirectory();
        const string corrupt = "{\"Version\":1,\"Characters\":[{\"CharacterId\":1,\"FreeCompanyId\":10,\"CharacterName\":\"One\",\"World\":\"Cerberus\",\"ObservedAtUtc\":\"2026-10-26T10:00:00Z\",\"IsLive\":false}],\"Chests\":[]}";
        File.WriteAllText(directory.Path, corrupt);
        var warnings = new List<string>();
        var store = new JsonBalanceObservationStore(directory.Path);
        Assert.Empty(store.Load(warnings).Characters);
        var preserved = Assert.Single(Directory.GetFiles(directory.Directory, "gil-balances.corrupt-*.json"));
        Assert.Equal(corrupt, File.ReadAllText(preserved));
        Assert.True(store.Save(GilBalanceSnapshot.Empty, warnings));
    }

    [Fact]
    public void FailedAtomicWritePreservesPreviousFileAndDeduplicatesWarnings()
    {
        using var directory = new TempDirectory();
        const string original = "{\"Version\":1,\"Characters\":[],\"Chests\":[]}";
        File.WriteAllText(directory.Path, original);
        var store = new JsonBalanceObservationStore(directory.Path, new FailingWriter(), () => Now);
        var warnings = new List<string>();
        store.Load(warnings);
        Assert.False(store.Save(GilBalanceSnapshot.Empty, warnings));
        Assert.False(store.Save(GilBalanceSnapshot.Empty, warnings));
        Assert.Equal(original, File.ReadAllText(directory.Path));
        Assert.Single(warnings);
    }

    private sealed class FailingWriter : IAtomicFileWriter
    {
        public void Write(string destinationPath, Action<Stream> writeContent) => throw new IOException("Test failure");
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Directory { get; } = System.IO.Directory.CreateTempSubdirectory("seta-gil-store-").FullName;
        public string Path => System.IO.Path.Combine(Directory, "gil-balances.json");
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
