namespace SubmarineEtaPlanner.Planner;

public enum VoyageProgressState
{
    Planned,
    Underway,
    ReadyToCollect,
    Syncing,
    TargetReached,
}

public sealed record VoyageProgressPresentation(
    string Label,
    string Tooltip,
    int? VoyagesLeft,
    VoyageProgressState State);

public static class VoyageProgressFormatter
{
    public static VoyageProgressPresentation Create(
        PerSubEtaResult submarine,
        int targetRank,
        DateTimeOffset now)
    {
        if (submarine.StartingRank >= targetRank)
        {
            return new VoyageProgressPresentation(
                "0",
                "Target rank is already recorded. Any current farming voyage is not included.",
                0,
                VoyageProgressState.TargetReached);
        }

        if (submarine.CurrentVoyageUnknown)
        {
            return new VoyageProgressPresentation(
                "— · syncing",
                "The submarine appears to be away, but its route is not available yet. Wait for SubmarineTracker to sync before relying on the voyage count.",
                null,
                VoyageProgressState.Syncing);
        }

        var hasCurrentVoyage = submarine.CurrentRoute.Count > 0 && submarine.CurrentReturnAtUtc is not null;
        if (!hasCurrentVoyage)
        {
            var count = submarine.VoyageCount;
            return new VoyageProgressPresentation(
                count.ToString(),
                $"{FormatCount(count)} planned to reach rank {targetRank}.",
                count,
                VoyageProgressState.Planned);
        }

        var total = checked(submarine.VoyageCount + 1);
        var readyToCollect = submarine.CurrentReturnAtUtc!.Value <= now;
        var currentDescription = readyToCollect ? "ready to collect" : "underway";
        var plannedDescription = submarine.VoyageCount == 0
            ? string.Empty
            : $" and {FormatCount(submarine.VoyageCount)} planned after collection";
        var tooltip = $"{FormatCount(total)} left: 1 {currentDescription}{plannedDescription}.\n" +
                      "The current voyage remains counted until you collect it and SubmarineTracker records the actual EXP and rank.";

        return new VoyageProgressPresentation(
            $"{total} · {(readyToCollect ? "collect" : "underway")}",
            tooltip,
            total,
            readyToCollect ? VoyageProgressState.ReadyToCollect : VoyageProgressState.Underway);
    }

    private static string FormatCount(int count)
        => $"{count} voyage{(count == 1 ? string.Empty : "s")}";
}
