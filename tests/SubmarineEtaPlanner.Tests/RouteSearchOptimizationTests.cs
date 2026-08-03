using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class RouteSearchOptimizationTests
{
    [Fact]
    public void RankedSearchMatchesStableExhaustiveOracle()
    {
        var random = new Random(90480);
        var values = Enumerable.Range(0, 400)
            .Select(routeId => new RouteRankValue(
                routeId,
                10_000 + random.NextDouble() * 5_000,
                TimeSpan.FromMinutes(random.Next(600, 4_000)).Ticks))
            .ToArray();
        var byId = values.ToDictionary(value => value.RouteId);
        var ranking = ExactRouteRanking.Create(values);

        for (var request = 0; request < 500; request++)
        {
            var eligible = values
                .Where(_ => random.NextDouble() < 0.18)
                .Select(value => value.RouteId)
                .ToHashSet();
            var expected = FindExhaustive(values, eligible);

            var actual = ExactRouteRanking.FindBest(
                ranking,
                eligible.Contains,
                routeId => byId[routeId],
                () => false,
                () => { },
                out _,
                out var completed);

            Assert.True(completed);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void RankedSearchReplaysScoreTieBandInStableOrder()
    {
        RouteRankValue[] values =
        [
            new(0, 100.0000, 100),
            new(1, 100.0005, 200),
            new(2, 99.9999, 50),
            new(3, 90.0000, 1),
        ];
        var byId = values.ToDictionary(value => value.RouteId);

        var actual = ExactRouteRanking.FindBest(
            ExactRouteRanking.Create(values),
            _ => true,
            routeId => byId[routeId],
            () => false,
            () => { },
            out var inspected,
            out var completed);

        Assert.True(completed);
        Assert.Equal(0, actual);
        Assert.Equal(4, inspected);
    }

    [Fact]
    public void RankedSearchMatchesOracleAcrossDenseScoreTies()
    {
        var random = new Random(4080);
        var values = Enumerable.Range(0, 180)
            .Select(routeId => new RouteRankValue(
                routeId,
                500.0 + (routeId % 11) * 0.0002,
                random.Next(1, 10_000)))
            .ToArray();
        var byId = values.ToDictionary(value => value.RouteId);
        var ranking = ExactRouteRanking.Create(values);

        for (var request = 0; request < 200; request++)
        {
            var eligible = values
                .Where(_ => random.NextDouble() < 0.25)
                .Select(value => value.RouteId)
                .ToHashSet();

            var actual = ExactRouteRanking.FindBest(
                ranking,
                eligible.Contains,
                routeId => byId[routeId],
                () => false,
                () => { },
                out _,
                out var completed);

            Assert.True(completed);
            var expected = FindExhaustive(values, eligible);
            Assert.True(
                expected == actual,
                $"request={request}, expected={expected}, actual={actual}, " +
                $"expectedValue={byId[expected!.Value]}, actualValue={byId[actual!.Value]}");
        }
    }

    [Fact]
    public void RankedSearchDoesNotCompleteAfterDeadline()
    {
        var values = Enumerable.Range(0, 2_000)
            .Select(routeId => new RouteRankValue(routeId, 2_000 - routeId, routeId + 1))
            .ToArray();
        var byId = values.ToDictionary(value => value.RouteId);
        var stop = false;

        var actual = ExactRouteRanking.FindBest(
            ExactRouteRanking.Create(values),
            routeId =>
            {
                if (routeId >= 1_023)
                    stop = true;
                return false;
            },
            routeId => byId[routeId],
            () => stop,
            () => { },
            out var inspected,
            out var completed);

        Assert.False(completed);
        Assert.Null(actual);
        Assert.Equal(1_024, inspected);
    }

    [Fact]
    public void EntryBoundedCacheEvictsOnlyLeastRecentlyUsedItem()
    {
        var cache = new BoundedLruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        Assert.True(cache.TryGetValue("a", out _));

        var evictions = cache.Set("c", 3);

        Assert.Equal(1, evictions);
        Assert.True(cache.TryGetValue("a", out var a));
        Assert.False(cache.TryGetValue("b", out _));
        Assert.True(cache.TryGetValue("c", out var c));
        Assert.Equal(1, a);
        Assert.Equal(3, c);
    }

    [Fact]
    public void WeightBoundedCacheRemainsWithinBudget()
    {
        var cache = new BoundedLruCache<string, int[]>(
            100,
            maximumWeight: 6,
            values => values.Length);
        cache.Set("a", [1, 2, 3]);
        cache.Set("b", [4, 5]);

        var evictions = cache.Set("c", [6, 7, 8]);

        Assert.Equal(1, evictions);
        Assert.True(cache.CurrentWeight <= 6);
        Assert.False(cache.TryGetValue("a", out _));
        Assert.True(cache.TryGetValue("b", out _));
        Assert.True(cache.TryGetValue("c", out _));
    }

    private static int? FindExhaustive(
        IReadOnlyList<RouteRankValue> values,
        IReadOnlySet<int> eligible)
    {
        RouteRankValue? best = null;
        foreach (var candidate in values.OrderBy(value => value.RouteId))
        {
            if (!eligible.Contains(candidate.RouteId) ||
                (best is not null && !ExactRouteRanking.IsBetter(candidate, best.Value)))
            {
                continue;
            }

            best = candidate;
        }

        return best?.RouteId;
    }
}
