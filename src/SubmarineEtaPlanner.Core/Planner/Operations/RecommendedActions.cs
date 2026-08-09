namespace SubmarineEtaPlanner.Planner;

public enum RecommendedAction
{
    None,
    WaitForTracker,
    ChooseFarmingRoute,
    CollectThenWaitForTracker,
    SendLevelingRouteNow,
    SendLevelingRouteAfterCollection,
    CollectAndResendFarmingRouteNow,
    SendFarmingRouteNow,
    ResendFarmingRouteAfterCollection,
    Paused,
}

public static class RecommendedActionFormatter
{
    public static string Format(RecommendedAction action)
        => action switch
        {
            RecommendedAction.None => "No action",
            RecommendedAction.WaitForTracker => "Wait for SubmarineTracker synchronization",
            RecommendedAction.ChooseFarmingRoute => "Choose farming route",
            RecommendedAction.CollectThenWaitForTracker => "Collect now; send the modeled route after synchronization",
            RecommendedAction.SendLevelingRouteNow => "Send recommended leveling route now",
            RecommendedAction.SendLevelingRouteAfterCollection => "Send recommended leveling route after collection",
            RecommendedAction.CollectAndResendFarmingRouteNow => "Collect and resend farming route now",
            RecommendedAction.SendFarmingRouteNow => "Send farming route now",
            RecommendedAction.ResendFarmingRouteAfterCollection => "Resend farming route after collection",
            RecommendedAction.Paused => "Paused",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown recommended action."),
        };
}
