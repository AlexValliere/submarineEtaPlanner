using SubmarineEtaPlanner.Planner;
using SubmarineEtaPlanner.SubmarineTrackerCompat;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class BuildResolverTests
{
    [Theory]
    [InlineData(1, "SSSS")]
    [InlineData(14, "SSSS")]
    [InlineData(15, "SSUS")]
    [InlineData(24, "SSUS")]
    [InlineData(25, "SSUW")]
    [InlineData(113, "SSUW")]
    [InlineData(114, "WSCC")]
    public void ReturnsDefaultBuildForRankBoundaries(int rank, string expected)
    {
        var resolver = new BuildResolver(new CompatSubmarineCatalog());
        var settings = EtaSettings.CreateDefault();

        Assert.Equal(expected, resolver.GetBuildCodeForRank(rank, settings));
    }
}
