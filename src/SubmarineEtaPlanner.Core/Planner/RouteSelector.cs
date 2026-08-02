namespace SubmarineEtaPlanner.Planner;

public sealed class RouteSelector(ISubmarineCatalog catalog, RouteUnlockGraph unlockGraph)
{
    public RouteCandidate SelectNextRoute(
        SubmarineState submarine,
        UnlockState unlockState,
        SubmarineBuild build,
        EtaSettings settings,
        bool fleetMode,
        DateTimeOffset? deadlineUtc = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var objective = unlockGraph.GetNextObjective(
            unlockState,
            settings,
            build.Rank,
            settings.TargetRank,
            fleetMode);

        if (objective is not null)
        {
            var objectiveResult = FindBest(
                build,
                unlockState,
                SectorMask.From([objective.RequiredPoint]),
                settings,
                deadlineUtc,
                cancellationToken);
            if (objectiveResult is not null)
            {
                return ReservePendingUnlocks(
                    objectiveResult with { AdvancesUnlockObjective = true },
                    unlockState,
                    fleetMode);
            }
        }

        var fallback = FindBest(
            build,
            unlockState,
            new SectorMask(),
            settings,
            deadlineUtc,
            cancellationToken);
        if (fallback is not null)
            return ReservePendingUnlocks(fallback, unlockState, fleetMode);

        return new RouteCandidate(
            [],
            0,
            TimeSpan.Zero,
            0,
            [],
            settings.EtaModel,
            settings.GetEffectiveDurationLimitHours() > 0);
    }

    private RouteCandidate? FindBest(
        SubmarineBuild build,
        UnlockState unlockState,
        SectorMask mustInclude,
        EtaSettings settings,
        DateTimeOffset? deadlineUtc,
        CancellationToken cancellationToken)
        => catalog.FindBestRoute(new RouteSearchRequest(
            build,
            unlockState.UnlockedPoints,
            SectorMask.From(unlockState.UnlockedPoints),
            mustInclude,
            settings,
            SectorMask.From(unlockGraph.GetSaturatedUnlockSourcePoints(unlockState, settings)),
            deadlineUtc,
            cancellationToken)).Route;

    private RouteCandidate ReservePendingUnlocks(RouteCandidate route, UnlockState unlockState, bool fleetMode)
    {
        var unlocks = unlockGraph.GetUnlockTargetsForRoute(route.Route, unlockState, rank: int.MaxValue);
        if (fleetMode)
            unlockGraph.ReserveRoute(route.Route, unlockState);

        return route with { UnlockTargets = unlocks };
    }
}
