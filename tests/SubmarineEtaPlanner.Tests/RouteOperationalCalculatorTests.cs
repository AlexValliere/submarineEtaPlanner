using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class RouteOperationalCalculatorTests
{
    private static readonly SubmarineBuild Build = new("SSUW", 90, 0, 0, 0, 0, 150);

    [Fact]
    public void CalculateFuelReturnsCompleteTankTotal()
    {
        var calculator = CreateCalculator(new Dictionary<uint, int>
        {
            [1] = 5,
            [2] = 8,
            [3] = 13,
        });

        var result = calculator.CalculateFuel([1, 2, 3]);

        Assert.Equal(26, result.CeruleumTanks);
        Assert.True(result.IsComplete);
        Assert.Empty(result.UnknownSectors);
    }

    [Fact]
    public void CalculateFuelCountsDuplicateSectorsOnce()
    {
        var calculator = CreateCalculator(new Dictionary<uint, int>
        {
            [1] = 5,
            [2] = 8,
        });

        var result = calculator.CalculateFuel([1, 2, 1, 2]);

        Assert.Equal(13, result.CeruleumTanks);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void CalculateFuelReportsUnknownSectorsAndKnownPartialTotal()
    {
        var calculator = CreateCalculator(new Dictionary<uint, int>
        {
            [1] = 5,
        });

        var result = calculator.CalculateFuel([7, 1, 7, 9]);

        Assert.Equal(5, result.CeruleumTanks);
        Assert.False(result.IsComplete);
        Assert.Equal([7u, 9u], result.UnknownSectors);
    }

    [Fact]
    public void AnalyzeOrderedRouteReturnsCompleteEmptyProfileWithoutDelegatingDuration()
    {
        var durationDelegated = false;
        var calculator = new RouteOperationalCalculator(
            new Dictionary<uint, int>(),
            (_, _) =>
            {
                durationDelegated = true;
                return TimeSpan.FromHours(1);
            });

        var result = calculator.AnalyzeOrderedRoute([], Build);

        Assert.Empty(result.Route);
        Assert.Equal(0, result.Fuel.CeruleumTanks);
        Assert.True(result.Fuel.IsComplete);
        Assert.Empty(result.Fuel.UnknownSectors);
        Assert.Equal(TimeSpan.Zero, result.Duration);
        Assert.False(durationDelegated);
    }

    [Fact]
    public void CalculateFuelThrowsOnOverflow()
    {
        var calculator = CreateCalculator(new Dictionary<uint, int>
        {
            [1] = int.MaxValue,
            [2] = 1,
        });

        Assert.Throws<OverflowException>(() => calculator.CalculateFuel([1, 2]));
    }

    [Fact]
    public void AnalyzeOrderedRoutePreservesOrderWhenDelegatingDuration()
    {
        IReadOnlyList<uint>? delegatedRoute = null;
        SubmarineBuild? delegatedBuild = null;
        var expectedDuration = TimeSpan.FromHours(17);
        var calculator = new RouteOperationalCalculator(
            new Dictionary<uint, int>
            {
                [1] = 5,
                [2] = 8,
            },
            (route, build) =>
            {
                delegatedRoute = route.ToArray();
                delegatedBuild = build;
                return expectedDuration;
            });

        var result = calculator.AnalyzeOrderedRoute([2, 1, 2], Build);

        Assert.Equal([2u, 1u, 2u], result.Route);
        Assert.Equal([2u, 1u, 2u], delegatedRoute);
        Assert.Same(Build, delegatedBuild);
        Assert.Equal(13, result.Fuel.CeruleumTanks);
        Assert.Equal(expectedDuration, result.Duration);
    }

    private static RouteOperationalCalculator CreateCalculator(
        IReadOnlyDictionary<uint, int> tankRequirementBySector)
        => new(tankRequirementBySector, (_, _) => TimeSpan.Zero);
}
