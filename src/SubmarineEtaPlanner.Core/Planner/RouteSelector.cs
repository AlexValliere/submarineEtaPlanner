namespace SubmarineEtaPlanner.Planner;

public sealed class RouteSelector(ISubmarineCatalog catalog, RouteUnlockGraph unlockGraph)
{
    public RouteCandidate SelectNextRoute(
        SubmarineState submarine,
        UnlockState unlockState,
        SubmarineBuild build,
        EtaSettings settings,
        bool fleetMode,
        DateTimeOffset? deadlineUtc = null)
    {
        var rank = build.Rank;
        var requiredUnlockPoints = GetRequiredUnlockPoints(unlockState, settings, rank, fleetMode);

        if (requiredUnlockPoints.Count > 0)
        {
            var unlockCandidate = catalog.GetCandidateRoutes(
                    build,
                    unlockState.UnlockedPoints,
                    unlockState.ExploredPoints,
                    requiredUnlockPoints,
                    settings,
                    deadlineUtc)
                .OrderByDescending(c => settings.OptimizeExpPerHour ? c.ExpPerHour : c.Exp)
                .ThenBy(c => c.Duration)
                .FirstOrDefault(c => c.Route.Any(requiredUnlockPoints.Contains));

            if (unlockCandidate is not null)
                return ReservePendingUnlocks(unlockCandidate, unlockState, rank, fleetMode);
        }

        var fallback = catalog.GetCandidateRoutes(
                build,
                unlockState.UnlockedPoints,
                unlockState.ExploredPoints,
                new HashSet<uint>(),
                settings,
                deadlineUtc)
            .Where(c => !fleetMode || !c.UnlockTargets.Any(unlockState.PendingUnlockPoints.Contains))
            .OrderByDescending(c => settings.OptimizeExpPerHour ? c.ExpPerHour : c.Exp)
            .ThenBy(c => c.Duration)
            .FirstOrDefault();

        if (fallback is not null)
            return ReservePendingUnlocks(fallback, unlockState, rank, fleetMode);

        return new RouteCandidate([], 0, TimeSpan.Zero, 0, []);
    }

    private HashSet<uint> GetRequiredUnlockPoints(
        UnlockState unlockState,
        EtaSettings settings,
        int rank,
        bool fleetMode)
    {
        IEnumerable<uint> candidates = settings.RouteGoal switch
        {
            RouteGoal.UnlockSubSlotsThenLevel => GetSubSlotUnlockPathCandidates(unlockState.UnlockedPoints, rank),
            RouteGoal.UnlockEverythingThenLevel => unlockGraph.GetNextUnlockCandidates(
                unlockState.UnlockedPoints,
                rank,
                settings.PrioritizeSubSlots),
            _ => [],
        };

        return candidates
            .Where(point => !fleetMode || !unlockGraph.GetUnlockTargetsForRoute([point], unlockState, rank).Any(unlockState.PendingUnlockPoints.Contains))
            .ToHashSet();
    }

    private IEnumerable<uint> GetSubSlotUnlockPathCandidates(IReadOnlySet<uint> unlockedPoints, int rank)
    {
        var rulesByTarget = catalog.UnlockRules.ToDictionary(rule => rule.UnlocksPoint, rule => rule);
        foreach (var slotRule in catalog.UnlockRules.Where(rule => rule.UnlocksSubSlot && !unlockedPoints.Contains(rule.UnlocksPoint)))
        {
            var path = unlockGraph.GetUnlockPath(slotRule.UnlocksPoint);
            foreach (var target in path.Skip(1))
            {
                if (!rulesByTarget.TryGetValue(target, out var rule))
                    continue;
                if (rule.RequiredRank > rank)
                    continue;
                if (!unlockedPoints.Contains(rule.SourcePoint) || unlockedPoints.Contains(rule.UnlocksPoint))
                    continue;

                yield return rule.SourcePoint;
                break;
            }
        }
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
