using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class FuelStockPresentationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 9, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LiveSourceUsesRequiredResultAndRunwayWording()
    {
        var stock = FuelStockResolver.Resolve(
            100,
            FuelStockMode.Automatic,
            null,
            0,
            [Observation(1, 100, "Alex", "Ragnarok", 2_415, Now, isLive: true)]);

        var presentation = FuelStockPresentation.Describe(stock, Now);

        Assert.Equal("Live — Alex@Ragnarok — 2,415 tanks", presentation.ResultLine);
        Assert.Null(presentation.DetailLine);
        Assert.Equal("Alex@Ragnarok — Live", presentation.SourceLine);
    }

    [Fact]
    public void StoredSourceIsVisiblyTimestamped()
    {
        var observation = Observation(1, 100, "Alt Name", "Cerberus", 1_802, Now.AddHours(-6));
        var stock = FuelStockResolver.Resolve(100, FuelStockMode.Automatic, null, 0, [observation]);

        var presentation = FuelStockPresentation.Describe(stock, Now);

        Assert.Equal("Last observed — Alt Name@Cerberus — 1,802 tanks", presentation.ResultLine);
        Assert.Equal("Observed 6 hours ago", presentation.DetailLine);
        Assert.Equal("Alt Name@Cerberus — Last observed 6h ago", presentation.SourceLine);
        Assert.Equal(
            "Alt Name@Cerberus — 1,802 tanks — Last observed 6h ago",
            FuelStockPresentation.FormatCandidate(observation, Now));
    }

    [Fact]
    public void CandidateListContainsOnlyLatestObservationForCharactersInSelectedFc()
    {
        var candidates = FuelStockPresentation.CandidatesForFreeCompany(
            100,
            [
                Observation(1, 100, "Alex", "Ragnarok", 100, Now.AddDays(-2)),
                Observation(1, 100, "Alex", "Ragnarok", 120, Now, isLive: true),
                Observation(2, 200, "Unrelated", "Cerberus", 999, Now, isLive: true),
            ]);

        var candidate = Assert.Single(candidates);
        Assert.Equal(1UL, candidate.CharacterId);
        Assert.Equal(120, candidate.CeruleumTanks);
        Assert.True(candidate.IsLive);
    }

    [Fact]
    public void MissingNumericFcIdHasNoObservedCandidates()
    {
        var candidates = FuelStockPresentation.CandidatesForFreeCompany(
            null,
            [Observation(1, 100, "Alex", "Ragnarok", 100, Now, isLive: true)]);

        Assert.Empty(candidates);
    }

    [Fact]
    public void ManualSourceIsAlwaysMarkedManual()
    {
        var stock = FuelStockResolver.Resolve(null, FuelStockMode.Manual, null, 2_400, []);

        var presentation = FuelStockPresentation.Describe(stock, Now);

        Assert.Equal("Manual — 2,400 tanks", presentation.ResultLine);
        Assert.Equal("Manual", presentation.SourceLine);
    }

    private static CharacterFuelObservation Observation(
        ulong characterId,
        ulong freeCompanyId,
        string name,
        string world,
        int tanks,
        DateTimeOffset observedAt,
        bool isLive = false) =>
        new(characterId, freeCompanyId, name, world, tanks, observedAt, isLive);
}
