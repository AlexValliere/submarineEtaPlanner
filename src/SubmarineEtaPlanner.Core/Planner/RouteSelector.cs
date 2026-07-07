namespace SubmarineEtaPlanner.Planner;

public sealed class RouteSelector(ISubmarineCatalog catalog, RouteUnlockGraph unlockGraph)
{
    public RouteCandidate SelectNextRoute(
        SubmarineState submarine,
        UnlockState unlockState,
        SubmarineBuild build,
        EtaSettings settings,
        bool fleetMode)
    {
        var warnings = new List<string>();
        var requiredUnlockPoints = unlockGraph
            .GetNextUnlockCandidates(unlockState.UnlockedPoints, submarine.Rank, settings.PrioritizeSubSlots)
            .Where(point => !fleetMode || !unlockGraph.GetUnlockTargetsForRoute([point], unlockState, submarine.Rank).Any(unlockState.PendingUnlockPoints.Contains))
            .ToHashSet();

        var candidates = catalog.GetCandidateRoutes(
                build,
                unlockState.UnlockedPoints,
                unlockState.ExploredPoints,
                requiredUnlockPoints,
                settings)
            .OrderByDescending(c => c.ExpPerHour)
            .ThenBy(c => c.Duration)
            .ToArray();

        var unlockCandidate = candidates.FirstOrDefault(c => requiredUnlockPoints.Count > 0 && c.Route.Any(requiredUnlockPoints.Contains));
        if (unlockCandidate is not null)
            return ReservePendingUnlocks(unlockCandidate, unlockState, submarine.Rank, fleetMode);

        var fallback = catalog.GetCandidateRoutes(
                build,
                unlockState.UnlockedPoints,
                unlockState.ExploredPoints,
                new HashSet<uint>(),
                settings)
            .Where(c => !fleetMode || !c.UnlockTargets.Any(unlockState.PendingUnlockPoints.Contains))
            .OrderByDescending(c => settings.OptimizeExpPerHour ? c.ExpPerHour : c.Exp)
            .ThenBy(c => c.Duration)
            .FirstOrDefault();

        if (fallback is not null)
            return ReservePendingUnlocks(fallback, unlockState, submarine.Rank, fleetMode);

        warnings.Add("No valid route was found; using an empty idle plan.");
        return new RouteCandidate([], 0, TimeSpan.Zero, 0, []);
    }

    private RouteCandidate ReservePendingUnlocks(RouteCandidate route, UnlockState unlockState, int rank, bool fleetMode)
    {
        var unlocks = unlockGraph.GetUnlockTargetsForRoute(route.Route, unlockState, rank);
        if (fleetMode)
        {
            foreach (var unlock in unlocks)
                unlockState.PendingUnlockPoints.Add(unlock);
        }

        return route with { UnlockTargets = unlocks };
    }
}
