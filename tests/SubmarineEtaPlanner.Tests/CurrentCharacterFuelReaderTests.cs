using SubmarineEtaPlanner.Fuel;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class CurrentCharacterFuelReaderTests
{
    [Fact]
    public void ValidCharacterReturnsLiveImmutableObservationWithCallerTimestamp()
    {
        var data = new CurrentCharacterFuelData(
            CharacterId: 123,
            FreeCompanyId: 456,
            CharacterName: "Alpha Beta",
            World: "Cerberus",
            CeruleumTanks: 789);
        var suppliedAt = new DateTimeOffset(2026, 8, 9, 14, 30, 0, TimeSpan.FromHours(2));
        var reader = CreateReader(new FakeGameFuelInventoryReader(data));

        var observation = reader.TryRead(suppliedAt, []);

        Assert.NotNull(observation);
        Assert.Equal(data.CharacterId, observation.CharacterId);
        Assert.Equal(data.FreeCompanyId, observation.FreeCompanyId);
        Assert.Equal(data.CharacterName, observation.CharacterName);
        Assert.Equal(data.World, observation.World);
        Assert.Equal(data.CeruleumTanks, observation.CeruleumTanks);
        Assert.Equal(suppliedAt, observation.ObservedAtUtc);
        Assert.Equal(TimeSpan.Zero, observation.ObservedAtUtc.Offset);
        Assert.True(observation.IsLive);
        Assert.True(typeof(CharacterFuelObservation).IsSealed);
    }

    [Fact]
    public void NoPlayerReturnsNoObservation()
    {
        var reader = CreateReader(new FakeGameFuelInventoryReader((CurrentCharacterFuelData?)null));
        var warnings = new List<string>();

        var observation = reader.TryRead(DateTimeOffset.UtcNow, warnings);

        Assert.Null(observation);
        Assert.Empty(warnings);
    }

    [Fact]
    public void NoFreeCompanyIdReturnsNoObservation()
    {
        var reader = CreateReader(new FakeGameFuelInventoryReader(
            new CurrentCharacterFuelData(123, 0, "Alpha Beta", "Cerberus", 10)));
        var warnings = new List<string>();

        var observation = reader.TryRead(DateTimeOffset.UtcNow, warnings);

        Assert.Null(observation);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ZeroTanksIsAValidObservation()
    {
        var reader = CreateReader(new FakeGameFuelInventoryReader(
            new CurrentCharacterFuelData(123, 456, "Alpha Beta", "Cerberus", 0)));

        var observation = reader.TryRead(DateTimeOffset.UtcNow, []);

        Assert.NotNull(observation);
        Assert.Equal(0, observation.CeruleumTanks);
    }

    [Fact]
    public void ReaderExceptionIsLoggedWarnedAndReturnsNoObservation()
    {
        var exception = new InvalidOperationException("Inventory unavailable");
        var logs = new List<(Exception Exception, string Message)>();
        var warnings = new List<string>();
        var reader = new CurrentCharacterFuelReader(
            new FakeGameFuelInventoryReader(exception),
            (loggedException, message) => logs.Add((loggedException, message)));

        var observation = reader.TryRead(DateTimeOffset.UtcNow, warnings);

        Assert.Null(observation);
        var log = Assert.Single(logs);
        Assert.Same(exception, log.Exception);
        Assert.Equal("Failed to read current character ceruleum stock.", log.Message);
        Assert.Equal(
            ["The current character's ceruleum tank count could not be read."],
            warnings);
    }

    [Fact]
    public void ZeroCharacterIdReturnsNoObservation()
    {
        var reader = CreateReader(new FakeGameFuelInventoryReader(
            new CurrentCharacterFuelData(0, 456, "Alpha Beta", "Cerberus", 10)));

        Assert.Null(reader.TryRead(DateTimeOffset.UtcNow, []));
    }

    private static CurrentCharacterFuelReader CreateReader(IGameFuelInventoryReader gameReader) =>
        new(gameReader, (_, _) => { });

    private sealed class FakeGameFuelInventoryReader : IGameFuelInventoryReader
    {
        private readonly CurrentCharacterFuelData? data;
        private readonly Exception? exception;

        public FakeGameFuelInventoryReader(CurrentCharacterFuelData? data)
        {
            this.data = data;
        }

        public FakeGameFuelInventoryReader(Exception exception)
        {
            this.exception = exception;
        }

        public CurrentCharacterFuelData? TryRead()
        {
            if (this.exception is not null)
                throw this.exception;

            return this.data;
        }
    }
}
