using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class RecommendedActionFormatterTests
{
    [Theory]
    [InlineData(RecommendedAction.None, "No action")]
    [InlineData(RecommendedAction.WaitForTracker, "Wait for SubmarineTracker synchronization")]
    [InlineData(RecommendedAction.ChooseFarmingRoute, "Choose farming route")]
    [InlineData(RecommendedAction.CollectThenWaitForTracker, "Collect now; send the modeled route after synchronization")]
    [InlineData(RecommendedAction.SendLevelingRouteNow, "Send recommended leveling route now")]
    [InlineData(RecommendedAction.SendLevelingRouteAfterCollection, "Send recommended leveling route after collection")]
    [InlineData(RecommendedAction.CollectAndResendFarmingRouteNow, "Collect and resend farming route now")]
    [InlineData(RecommendedAction.SendFarmingRouteNow, "Send farming route now")]
    [InlineData(RecommendedAction.ResendFarmingRouteAfterCollection, "Resend farming route after collection")]
    [InlineData(RecommendedAction.Paused, "Paused")]
    public void FormatsExactUserFacingWording(RecommendedAction action, string expected)
    {
        Assert.Equal(expected, RecommendedActionFormatter.Format(action));
    }

    [Fact]
    public void RejectsUnknownAction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecommendedActionFormatter.Format((RecommendedAction)int.MaxValue));
    }
}
