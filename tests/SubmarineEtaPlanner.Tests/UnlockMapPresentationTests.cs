using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class UnlockMapPresentationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ClassifiesStatesRespectsSiblingOrderAndBuildsPaths()
    {
        var destinations = Destinations();
        UnlockRule[] rules =
        [
            new(1, 2, 1, 1),
            new(2, 3, 5, 5),
            new(2, 4, 5, 5),
            new(3, 5, 8, 8, UnlocksSubSlot: true, UnlocksMap: true),
        ];
        var fc = CreateFc(
            unlocked: [1, 2],
            explored: [1],
            CreateSub(1, "Voyager", rank: 10));

        var result = UnlockMapPresentationBuilder.Build(fc, destinations, rules, Now);

        Assert.Equal(5, result.TotalDestinations);
        Assert.Equal(2, result.UnlockedDestinations);
        Assert.Equal(1, result.ExploredDestinations);
        Assert.Equal(3, result.RemainingDestinations);
        Assert.Equal(UnlockDestinationState.Explored, Find(result, 1).State);
        Assert.Equal(UnlockDestinationState.Unlocked, Find(result, 2).State);
        Assert.Equal(UnlockDestinationState.Discoverable, Find(result, 3).State);
        Assert.Equal(UnlockDestinationState.Locked, Find(result, 4).State);
        Assert.Equal(UnlockDestinationBlockReason.EarlierSibling, Find(result, 4).BlockReason);
        Assert.Equal(3u, Find(result, 4).BlockingPoint);
        Assert.Equal([1u, 2u, 3u, 4u], Find(result, 4).PrerequisitePath);
        Assert.Equal(UnlockDestinationState.Locked, Find(result, 5).State);
        Assert.Equal(UnlockDestinationBlockReason.SourceLocked, Find(result, 5).BlockReason);
        Assert.Equal([1u, 2u, 3u, 5u], Find(result, 5).PrerequisitePath);
        Assert.True(Find(result, 5).IncomingRule!.UnlocksSubSlot);
        Assert.True(Find(result, 5).IncomingRule!.UnlocksMap);
    }

    [Fact]
    public void DiscoverableRequiresAHighEnoughFleetSubmarine()
    {
        var fc = CreateFc(
            unlocked: [1, 2],
            explored: [1],
            CreateSub(1, "Low rank", rank: 4));

        var result = UnlockMapPresentationBuilder.Build(
            fc,
            Destinations(),
            [new UnlockRule(1, 2, 1, 1), new UnlockRule(2, 3, 5, 5)],
            Now);

        Assert.Equal(UnlockDestinationState.Locked, Find(result, 3).State);
        Assert.Equal(UnlockDestinationBlockReason.FleetRank, Find(result, 3).BlockReason);
    }

    [Fact]
    public void ActiveAttemptsUseOnlyKnownFutureVoyagesAndNextSibling()
    {
        var active = CreateSub(1, "Active", 10, Now.AddHours(2), [2], currentVoyageKnown: true);
        var returned = CreateSub(2, "Returned", 10, Now.AddMinutes(-1), [2], currentVoyageKnown: true);
        var unknown = CreateSub(3, "Unknown", 10, Now.AddHours(3), [2], currentVoyageKnown: false);
        var fc = CreateFc([1, 2], [1], active, returned, unknown);
        UnlockRule[] rules = [new(1, 2, 1, 1), new(2, 3, 1, 1), new(2, 4, 1, 1)];

        var result = UnlockMapPresentationBuilder.Build(fc, Destinations(), rules, Now);

        var attempt = Assert.Single(Find(result, 3).ActiveAttempts);
        Assert.Equal("Active", attempt.SubmarineName);
        Assert.False(Find(result, 4).HasActiveAttempt);
    }

    [Fact]
    public void UnknownUnlockDataSuppressesCountsAndStates()
    {
        var fc = CreateFc([], [], CreateSub(1, "Sub", 10)) with { UnlockDataKnown = false };

        var result = UnlockMapPresentationBuilder.Build(
            fc,
            Destinations(),
            [new UnlockRule(1, 2, 1, 1)],
            Now);

        Assert.Null(result.UnlockedDestinations);
        Assert.Null(result.ExploredDestinations);
        Assert.Null(result.RemainingDestinations);
        Assert.All(result.Maps, map => Assert.Null(map.RemainingDestinations));
        Assert.All(result.Maps.SelectMany(map => map.Destinations), destination =>
            Assert.Equal(UnlockDestinationState.Unknown, destination.State));
    }

    [Fact]
    public void GroupsMapsAndIncludesCrossMapConnectionOnBothSides()
    {
        var fc = CreateFc([1, 2, 3], [1], CreateSub(1, "Sub", 10));
        UnlockRule[] rules = [new(1, 2, 1, 1), new(2, 3, 1, 1), new(3, 5, 1, 1)];

        var result = UnlockMapPresentationBuilder.Build(fc, Destinations(), rules, Now);

        Assert.Equal(2, result.Maps.Count);
        var first = result.Maps[0];
        var second = result.Maps[1];
        Assert.Contains(first.Connections, connection => connection is { SourcePoint: 3, TargetPoint: 5, CrossesMaps: true });
        Assert.Contains(second.Connections, connection => connection is { SourcePoint: 3, TargetPoint: 5, CrossesMaps: true });
    }

    [Fact]
    public void NormalizesCoordinatesPreservesAspectAndInvertsZ()
    {
        RouteDestination[] destinations =
        [
            Destination(1, 1, 0, 0),
            Destination(2, 1, 10, 20),
        ];

        var result = UnlockMapLayoutCalculator.Calculate(destinations, 120, 220, 10);

        Assert.Equal(new UnlockMapCanvasPoint(10, 210), result[1]);
        Assert.Equal(new UnlockMapCanvasPoint(110, 10), result[2]);
    }

    [Fact]
    public void CentersCoordinateContentWhenCanvasAspectDiffers()
    {
        RouteDestination[] destinations =
        [
            Destination(1, 1, 0, 0),
            Destination(2, 1, 10, 10),
        ];

        var result = UnlockMapLayoutCalculator.Calculate(destinations, 200, 100, 10);

        Assert.Equal(new UnlockMapCanvasPoint(60, 90), result[1]);
        Assert.Equal(new UnlockMapCanvasPoint(140, 10), result[2]);
    }

    [Fact]
    public void MissingAndDegenerateCoordinatesUseDeterministicGrid()
    {
        var missing = new RouteDestination(1, "A", "A", 1, "Map", 1);
        var positioned = Destination(2, 1, 5, 5);

        var first = UnlockMapLayoutCalculator.Calculate([missing, positioned], 100, 100, 10);
        var second = UnlockMapLayoutCalculator.Calculate([positioned, missing], 100, 100, 10);
        var single = UnlockMapLayoutCalculator.Calculate([positioned], 100, 100, 10);

        Assert.Equal(first[1], second[1]);
        Assert.Equal(first[2], second[2]);
        Assert.Equal(new UnlockMapCanvasPoint(50, 50), single[2]);
        Assert.All(first.Values, point =>
        {
            Assert.InRange(point.X, 10, 90);
            Assert.InRange(point.Y, 10, 90);
        });
    }

    private static UnlockMapDestinationPresentation Find(FcUnlockMapsPresentation presentation, uint sectorId)
        => presentation.Maps.SelectMany(map => map.Destinations).Single(destination => destination.Destination.SectorId == sectorId);

    private static RouteDestination[] Destinations()
        =>
        [
            Destination(1, 1, 0, 0),
            Destination(2, 1, 10, 5),
            Destination(3, 1, 20, 10),
            Destination(4, 1, 20, 0),
            Destination(5, 2, 0, 0),
        ];

    private static RouteDestination Destination(uint sectorId, uint mapId, float x, float z)
        => new(sectorId, $"S{sectorId}", $"Sector {sectorId}", mapId, $"Map {mapId}", 1)
        {
            MapPosition = new RouteMapPosition(x, z),
        };

    private static FcState CreateFc(
        IEnumerable<uint> unlocked,
        IEnumerable<uint> explored,
        params SubmarineState[] submarines)
        => new([1, 2, 3], "TEST", "World", unlocked.ToHashSet(), explored.ToHashSet(), submarines);

    private static SubmarineState CreateSub(
        long id,
        string name,
        int rank,
        DateTimeOffset? returnAt = null,
        IReadOnlyList<uint>? route = null,
        bool currentVoyageKnown = true)
        => new(
            [1, 2, 3],
            id,
            name,
            rank,
            0,
            1000,
            SubmarineBuildParts.Empty,
            returnAt ?? DateTimeOffset.UnixEpoch,
            route ?? [],
            currentVoyageKnown,
            []);
}
