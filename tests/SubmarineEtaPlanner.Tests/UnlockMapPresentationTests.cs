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
        Assert.Empty(Find(result, 1).RemainingUnlockPath);
        Assert.Empty(Find(result, 2).RemainingUnlockPath);
        Assert.Equal(UnlockDestinationState.Discoverable, Find(result, 3).State);
        Assert.Equal([2u, 3u], Find(result, 3).RemainingUnlockPath);
        Assert.Equal(UnlockDestinationState.Locked, Find(result, 4).State);
        Assert.Equal(UnlockDestinationBlockReason.EarlierSibling, Find(result, 4).BlockReason);
        Assert.Equal(3u, Find(result, 4).BlockingPoint);
        Assert.Equal([2u, 3u, 4u], Find(result, 4).RemainingUnlockPath);
        Assert.Equal(UnlockDestinationState.Locked, Find(result, 5).State);
        Assert.Equal(UnlockDestinationBlockReason.SourceLocked, Find(result, 5).BlockReason);
        Assert.Equal([2u, 3u, 5u], Find(result, 5).RemainingUnlockPath);
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
        Assert.All(result.Maps.SelectMany(map => map.Destinations), destination =>
            Assert.Empty(destination.RemainingUnlockPath));
    }

    [Fact]
    public void RemainingPathTreatsExploredPointsAsAccessible()
    {
        var fc = CreateFc([1, 2], [1, 3], CreateSub(1, "Sub", 10));
        UnlockRule[] rules = [new(1, 2, 1, 1), new(2, 3, 1, 1), new(3, 5, 1, 1)];

        var result = UnlockMapPresentationBuilder.Build(fc, Destinations(), rules, Now);

        Assert.Equal([3u, 5u], Find(result, 5).RemainingUnlockPath);
    }

    [Fact]
    public void RemainingPathFallsBackToCompleteStructuralPathWithoutAccessiblePoint()
    {
        var fc = CreateFc([], [], CreateSub(1, "Sub", 10));
        UnlockRule[] rules = [new(1, 2, 1, 1), new(2, 3, 1, 1)];

        var result = UnlockMapPresentationBuilder.Build(fc, Destinations(), rules, Now);

        Assert.Equal([1u, 2u, 3u], Find(result, 3).RemainingUnlockPath);
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
        Assert.Equal([3u, 5u], Find(result, 5).RemainingUnlockPath);
    }

    [Fact]
    public void LayoutIsDeterministicRegardlessOfInputOrder()
    {
        RouteDestination[] destinations =
        [
            Destination(1, 1, 0, 0),
            Destination(2, 1, 1, 0.2f),
            Destination(3, 1, 2, 0.6f),
            Destination(4, 1, 8, 7),
            Destination(5, 1, 9, 9),
        ];
        UnlockMapConnection[] connections =
        [
            Connection(1, 2),
            Connection(2, 3),
            Connection(3, 4),
            Connection(4, 5),
        ];

        var first = UnlockMapLayoutCalculator.Calculate(destinations, connections, 700, 420, 35, 18);
        var second = UnlockMapLayoutCalculator.Calculate(destinations.Reverse().ToArray(), connections.Reverse().ToArray(), 700, 420, 35, 18);

        Assert.Equal(first.OrderBy(pair => pair.Key), second.OrderBy(pair => pair.Key));
    }

    [Fact]
    public void UsesIndependentAxesAndInvertsZ()
    {
        RouteDestination[] destinations =
        [
            Destination(1, 1, 0, 0),
            Destination(2, 1, 10, 20),
        ];

        var result = UnlockMapLayoutCalculator.Calculate(destinations, [], 300, 150, 20, 5);

        Assert.True(result[1].X < result[2].X);
        Assert.True(result[1].Y > result[2].Y);
        Assert.True(result.Values.Max(point => point.X) - result.Values.Min(point => point.X) > 240);
        Assert.True(result.Values.Max(point => point.Y) - result.Values.Min(point => point.Y) > 90);
    }

    [Fact]
    public void CrowdedMapMaintainsMinimumAchievableSpacingAndBounds()
    {
        var destinations = Enumerable.Range(1, 20)
            .Select(index => index == 20
                ? Destination((uint)index, 1, 100, 100)
                : Destination((uint)index, 1, (index % 5) * 0.3f, (index / 5) * 0.3f))
            .ToArray();
        var connections = Enumerable.Range(1, 19)
            .Select(index => Connection((uint)index, (uint)(index + 1)))
            .ToArray();

        var result = UnlockMapLayoutCalculator.Calculate(destinations, connections, 900, 520, 40, 18);

        Assert.All(result.Values, point =>
        {
            Assert.InRange(point.X, 58, 842);
            Assert.InRange(point.Y, 58, 462);
        });
        Assert.True(MinimumDistance(result.Values) >= 47f, $"Minimum distance was {MinimumDistance(result.Values):N2}px.");
    }

    [Fact]
    public void ConnectedNodesArePulledCloserThanUnconnectedNodes()
    {
        RouteDestination[] destinations =
        [
            Destination(1, 1, 0, 0),
            Destination(2, 1, 10, 10),
        ];

        var disconnected = UnlockMapLayoutCalculator.Calculate(destinations, [], 700, 420, 35, 18);
        var connected = UnlockMapLayoutCalculator.Calculate(destinations, [Connection(1, 2)], 700, 420, 35, 18);

        Assert.True(Distance(connected[1], connected[2]) < Distance(disconnected[1], disconnected[2]) * 0.5f);
        Assert.True(Distance(connected[1], connected[2]) < 180f);
    }

    [Fact]
    public void CrossMapConnectionsDoNotInfluenceLayout()
    {
        RouteDestination[] destinations =
        [
            Destination(1, 1, 0, 0),
            Destination(2, 1, 10, 10),
        ];

        var withoutConnection = UnlockMapLayoutCalculator.Calculate(destinations, [], 700, 420, 35, 18);
        var withCrossMapConnection = UnlockMapLayoutCalculator.Calculate(
            destinations,
            [new UnlockMapConnection(1, 2, 1, 2)],
            700,
            420,
            35,
            18);

        Assert.Equal(withoutConnection.OrderBy(pair => pair.Key), withCrossMapConnection.OrderBy(pair => pair.Key));
    }

    [Fact]
    public void UndersizedCanvasStillReturnsFiniteBoundedPoints()
    {
        var destinations = Enumerable.Range(1, 25)
            .Select(index => Destination((uint)index, 1, 1, 1))
            .ToArray();

        var result = UnlockMapLayoutCalculator.Calculate(destinations, [], 120, 80, 5, 12);

        Assert.Equal(destinations.Length, result.Count);
        Assert.All(result.Values, point =>
        {
            Assert.True(float.IsFinite(point.X));
            Assert.True(float.IsFinite(point.Y));
            Assert.InRange(point.X, 17, 103);
            Assert.InRange(point.Y, 17, 63);
        });
    }

    [Fact]
    public void MissingAndDegenerateCoordinatesUseDeterministicGrid()
    {
        var missing = new RouteDestination(1, "A", "A", 1, "Map", 1);
        var positioned = Destination(2, 1, 5, 5);

        var first = UnlockMapLayoutCalculator.Calculate([missing, positioned], [], 100, 100, 10, 5);
        var second = UnlockMapLayoutCalculator.Calculate([positioned, missing], [], 100, 100, 10, 5);
        var single = UnlockMapLayoutCalculator.Calculate([positioned], [], 100, 100, 10, 5);

        Assert.Equal(first[1], second[1]);
        Assert.Equal(first[2], second[2]);
        Assert.Equal(new UnlockMapCanvasPoint(50, 50), single[2]);
        Assert.All(first.Values, point =>
        {
            Assert.InRange(point.X, 15, 85);
            Assert.InRange(point.Y, 15, 85);
        });
    }

    private static UnlockMapConnection Connection(uint sourcePoint, uint targetPoint)
        => new(sourcePoint, targetPoint, 1, 1);

    private static float MinimumDistance(IEnumerable<UnlockMapCanvasPoint> points)
    {
        var values = points.ToArray();
        var minimum = float.MaxValue;
        for (var first = 0; first < values.Length; first++)
        {
            for (var second = first + 1; second < values.Length; second++)
                minimum = Math.Min(minimum, Distance(values[first], values[second]));
        }
        return minimum;
    }

    private static float Distance(UnlockMapCanvasPoint first, UnlockMapCanvasPoint second)
        => MathF.Sqrt(MathF.Pow(second.X - first.X, 2) + MathF.Pow(second.Y - first.Y, 2));

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
