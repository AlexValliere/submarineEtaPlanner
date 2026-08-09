using SubmarineEtaPlanner.Planner;
using SubmarineEtaPlanner.SubmarineTrackerCompat;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class RouteSelectionCatalogTests
{
    private readonly CompatSubmarineCatalog catalog = new();

    [Fact]
    public void RunnableRoutePreservesPlayerOrdering()
    {
        var build = this.catalog.ResolveBuild("CCCC", 149);
        var unlocked = Enumerable.Range(1, 149).Select(value => (uint)value).ToHashSet();

        var result = this.catalog.ValidateRoute([3, 1, 2], build, unlocked);

        Assert.True(result.IsRunnable);
        Assert.Equal([3u, 1u, 2u], result.Route);
        Assert.NotNull(result.Duration);
    }

    [Fact]
    public void LockedAndAboveRankDestinationsAreRejected()
    {
        var build = this.catalog.ResolveBuild("SSSS", 5);

        var result = this.catalog.ValidateRoute([1, 10], build, new HashSet<uint> { 1 });

        Assert.False(result.IsRunnable);
        Assert.Contains(result.Errors, error => error.StartsWith("Not unlocked", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.StartsWith("Requires a higher rank", StringComparison.Ordinal));
    }

    [Fact]
    public void MixedMapsDuplicatesAndMoreThanFiveStopsAreRejected()
    {
        var build = this.catalog.ResolveBuild("CCCC", 149);
        var unlocked = Enumerable.Range(1, 149).Select(value => (uint)value).ToHashSet();

        var result = this.catalog.ValidateRoute([1, 1, 2, 3, 4, 31], build, unlocked);

        Assert.False(result.IsRunnable);
        Assert.Contains("A voyage can visit at most five destinations.", result.Errors);
        Assert.Contains("A destination can only be selected once.", result.Errors);
        Assert.Contains("Every destination must be on the same map.", result.Errors);
    }

    [Fact]
    public void RouteBeyondBuildRangeIsRejected()
    {
        var build = this.catalog.ResolveBuild("SSSS", 1) with { Range = 1 };

        var result = this.catalog.ValidateRoute([1], build, new HashSet<uint> { 1 });

        Assert.False(result.IsRunnable);
        Assert.Contains(result.Errors, error => error.Contains("current build", StringComparison.Ordinal));
    }

    [Fact]
    public void DestinationMetadataCanBeGroupedByMapAndFilteredByRank()
    {
        var destinations = this.catalog.RouteDestinations;

        Assert.NotEmpty(destinations);
        Assert.True(destinations.Select(destination => destination.MapId).Distinct().Count() > 1);
        Assert.All(destinations, destination => Assert.False(string.IsNullOrWhiteSpace(destination.Code)));
        Assert.Contains(destinations, destination => destination.RequiredRank <= 5);
    }
}
