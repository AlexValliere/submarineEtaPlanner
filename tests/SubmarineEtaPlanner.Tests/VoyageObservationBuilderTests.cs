using MessagePack;
using SubmarineEtaPlanner.Planner;
using SubmarineEtaPlanner.TrackerData;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class VoyageObservationBuilderTests
{
    private static readonly DateTimeOffset ReturnAtUtc = new(2026, 8, 9, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public void SubmarineStateDefaultsVoyageHistoryToEmpty()
    {
        var submarine = new SubmarineState(
            [],
            42,
            "Test",
            80,
            0,
            1,
            SubmarineBuildParts.Empty,
            ReturnAtUtc,
            [],
            true,
            []);

        Assert.Empty(submarine.VoyageHistory);
    }

    [Fact]
    public void MultipleSectorsBecomeOneVoyageAndDuplicateSectorsAreRemoved()
    {
        var observations = Build(
            Row(sectorId: 8),
            Row(sectorId: 3),
            Row(sectorId: 8));

        var observation = Assert.Single(observations);
        Assert.Equal(new uint[] { 3, 8 }, observation.SectorIds);
    }

    [Fact]
    public void PrimaryAndAdditionalQuantitiesAggregate()
    {
        var observations = Build(
            Row(primaryItemId: 22500, primaryItemCount: 2, additionalItemId: 22501, additionalItemCount: 3),
            Row(primaryItemId: 22501, primaryItemCount: 4, additionalItemId: 22500, additionalItemCount: 5));

        var observation = Assert.Single(observations);
        Assert.Collection(
            observation.Items,
            item =>
            {
                Assert.Equal(22500u, item.ItemId);
                Assert.Equal(7, item.Quantity);
            },
            item =>
            {
                Assert.Equal(22501u, item.ItemId);
                Assert.Equal(7, item.Quantity);
            });
        Assert.Equal(119_000, observation.GrossNpcGil);
    }

    [Fact]
    public void VoyageWithoutQualifyingGilItemsRemainsPresent()
    {
        var observations = Build(Row(primaryItemId: 5069, primaryItemCount: 999));

        var observation = Assert.Single(observations);
        Assert.Empty(observation.Items);
        Assert.Equal(0, observation.GrossNpcGil);
    }

    [Fact]
    public void SubmarinesWithSameReturnRemainSeparate()
    {
        var observations = Build(Row(submarineId: 2), Row(submarineId: 1));

        Assert.Equal([1L, 2L], observations.Select(observation => observation.SubmarineId));
    }

    [Fact]
    public void InconsistentStatsRetainFirstValuesAndProduceOneWarning()
    {
        var warnings = new List<string>();
        var rows = new[]
        {
            Row(rank: 80, surveillance: 100, retrieval: 101, favor: 102),
            Row(sectorId: 2, rank: 81, surveillance: 110, retrieval: 111, favor: 112),
            Row(sectorId: 3, rank: 82, surveillance: 120, retrieval: 121, favor: 122),
        };

        var observation = Assert.Single(VoyageObservationBuilder.Build(rows, KnownSalvageValueCatalog.Instance.Items, warnings));

        Assert.Equal(80, observation.Rank);
        Assert.Equal(100, observation.Surveillance);
        Assert.Equal(101, observation.Retrieval);
        Assert.Equal(102, observation.Favor);
        var warning = Assert.Single(warnings);
        Assert.Contains(observation.FcIdKey, warning);
        Assert.Contains("inconsistent rank or stats", warning);
    }

    [Fact]
    public void OutputIsDeterministicallyOrdered()
    {
        var later = ReturnAtUtc.AddHours(1);
        var firstFc = MessagePackSerializer.Serialize(1UL);
        var secondFc = MessagePackSerializer.Serialize(2UL);
        var rows = new[]
        {
            Row(fcId: secondFc, submarineId: 1, returnAtUtc: ReturnAtUtc, sectorId: 9),
            Row(fcId: firstFc, submarineId: 2, returnAtUtc: ReturnAtUtc, sectorId: 7,
                primaryItemId: 22501, primaryItemCount: 1),
            Row(fcId: firstFc, submarineId: 1, returnAtUtc: later, sectorId: 4,
                primaryItemId: 22501, primaryItemCount: 1),
            Row(fcId: firstFc, submarineId: 1, returnAtUtc: ReturnAtUtc, sectorId: 5,
                primaryItemId: 22501, primaryItemCount: 1, additionalItemId: 22500, additionalItemCount: 1),
            Row(fcId: firstFc, submarineId: 1, returnAtUtc: ReturnAtUtc, sectorId: 2,
                primaryItemId: 22500, primaryItemCount: 1),
        };

        var observations = Build(rows);

        Assert.Collection(
            observations,
            observation =>
            {
                Assert.Equal(Convert.ToHexString(firstFc), observation.FcIdKey);
                Assert.Equal(1, observation.SubmarineId);
                Assert.Equal(ReturnAtUtc, observation.ReturnAtUtc);
                Assert.Equal(new uint[] { 2, 5 }, observation.SectorIds);
                Assert.Equal(new uint[] { 22500, 22501 }, observation.Items.Select(item => item.ItemId));
            },
            observation =>
            {
                Assert.Equal(1, observation.SubmarineId);
                Assert.Equal(later, observation.ReturnAtUtc);
            },
            observation => Assert.Equal(2, observation.SubmarineId),
            observation => Assert.Equal(Convert.ToHexString(secondFc), observation.FcIdKey));
    }

    [Fact]
    public void DecodesGameFreeCompanyIdFromBlob()
    {
        const ulong gameFreeCompanyId = 9_876_543_210;

        var observation = Assert.Single(Build(Row(fcId: MessagePackSerializer.Serialize(gameFreeCompanyId))));

        Assert.Equal(gameFreeCompanyId, observation.GameFreeCompanyId);
    }

    private static IReadOnlyList<VoyageObservation> Build(params VoyageObservationRawRow[] rows)
        => Build((IEnumerable<VoyageObservationRawRow>)rows);

    private static IReadOnlyList<VoyageObservation> Build(IEnumerable<VoyageObservationRawRow> rows)
    {
        var warnings = new List<string>();
        var observations = VoyageObservationBuilder.Build(rows, KnownSalvageValueCatalog.Instance.Items, warnings);
        Assert.Empty(warnings);
        return observations;
    }

    private static VoyageObservationRawRow Row(
        byte[]? fcId = null,
        long submarineId = 42,
        DateTimeOffset? returnAtUtc = null,
        uint sectorId = 1,
        int rank = 80,
        int surveillance = 100,
        int retrieval = 101,
        int favor = 102,
        uint primaryItemId = 0,
        long primaryItemCount = 0,
        uint additionalItemId = 0,
        long additionalItemCount = 0)
        => new(
            fcId ?? MessagePackSerializer.Serialize(123UL),
            submarineId,
            returnAtUtc ?? ReturnAtUtc,
            sectorId,
            rank,
            surveillance,
            retrieval,
            favor,
            primaryItemId,
            primaryItemCount,
            additionalItemId,
            additionalItemCount);
}
