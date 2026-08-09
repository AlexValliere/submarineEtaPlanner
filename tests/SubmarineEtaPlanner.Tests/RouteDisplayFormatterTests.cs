using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class RouteDisplayFormatterTests
{
    [Fact]
    public void LocalizedDestinationNamesUseTrailingLetterCodes()
    {
        var names = new Dictionary<uint, string>
        {
            [1] = "Mer des noyades 4 (M)",
            [2] = "Repaire de l'armada (R)",
        };

        Assert.Equal("M → R", RouteDisplayFormatter.FormatCompactRoute([1, 2], point => names[point]));
    }

    [Theory]
    [InlineData("Destination (ab)", "AB")]
    [InlineData("Destination without code", "42")]
    [InlineData("(TOOLONG)", "42")]
    public void CompactCodeFallsBackToSectorIdWhenNeeded(string name, string expected)
        => Assert.Equal(expected, RouteDisplayFormatter.ExtractPointCode(42, name));
}
