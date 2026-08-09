using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class CurrentBuildCodeFormatterTests
{
    [Theory]
    [InlineData("S+C+U+S+", "SCUS++")]
    [InlineData("Y+W+S+C+", "YWSC++")]
    [InlineData("SCUS", "SCUS")]
    [InlineData("S+CUS", "S+CUS")]
    [InlineData("S+CU+S", "S+CU+S")]
    [InlineData("SCUS++", "SCUS++")]
    [InlineData("S+C+U+S", "S+C+U+S")]
    [InlineData("S+Q+U+S+", "S+Q+U+S+")]
    [InlineData("—", "—")]
    public void FormatsOnlyFullyUpgradedBuildCodes(string input, string expected)
        => Assert.Equal(expected, CurrentBuildCodeFormatter.Format(input));

    [Fact]
    public void PresentationUsesCompactCodeWithoutChangingTheSourceBuild()
    {
        var build = new SubmarineBuild("S+C+U+S+", 142, 0, 0, 0, 0, 0);

        var presentation = CurrentBuildPresentation.Create(build);

        Assert.Equal("SCUS++", presentation.Code);
        Assert.Equal("S+C+U+S+", build.Code);
    }
}
