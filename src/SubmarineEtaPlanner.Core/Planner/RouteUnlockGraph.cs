namespace SubmarineEtaPlanner.Planner;

public sealed class RouteUnlockGraph(ISubmarineCatalog catalog)
{
    public IReadOnlyList<uint> GetUnlockPath(uint targetPoint)
    {
        var reverse = catalog.UnlockRules.ToDictionary(rule => rule.UnlocksPoint, rule => rule.SourcePoint);
        var path = new Stack<uint>();
        var current = targetPoint;
        var guard = 0;

        path.Push(current);
        while (reverse.TryGetValue(current, out var parent) && guard++ < 256)
        {
            path.Push(parent);
            current = parent;
        }

        return path.ToArray();
    }

    public IReadOnlyList<uint> GetNextUnlockCandidates(
        IReadOnlySet<uint> unlockedPoints,
        int rank,
        bool prioritizeSubSlots)
    {
        var state = new UnlockState(
            new HashSet<uint>(unlockedPoints),
            new HashSet<uint>(unlockedPoints),
            [],
            [])
        {
            KnownSubmarineSlots = prioritizeSubSlots ? 1 : 4,
        };
        var settings = EtaSettings.CreateDefault() with
        {
            EtaModel = EtaModel.ExactRouteSearch,
            RouteGoal = RouteGoal.UnlockEverythingThenLevel,
            PrioritizeSubSlots = prioritizeSubSlots,
        };

        return GetReachableRules(state, settings, rank, int.MaxValue, fleetMode: false)
            .Select(rule => rule.SourcePoint)
            .Distinct()
            .ToArray();
    }

    public UnlockObjective? GetNextObjective(
        UnlockState state,
        EtaSettings settings,
        int rank,
        int targetRank,
        bool fleetMode)
    {
        var goal = settings.GetEffectiveRouteGoal();
        if (goal == RouteGoal.FastestLevelingOnly)
            return null;

        var chaseSlots = goal == RouteGoal.UnlockSubSlotsThenLevel || settings.PrioritizeSubSlots;
        if (chaseSlots && state.KnownSubmarineSlots < 4)
        {
            var slotObjective = GetSubmarineSlotObjective(state, settings, rank, fleetMode);
            if (slotObjective is not null)
                return slotObjective;
        }

        if (goal == RouteGoal.UnlockSubSlotsThenLevel)
            return null;

        var rule = GetReachableRules(state, settings, rank, targetRank, fleetMode).FirstOrDefault();
        if (rule is null)
            return null;

        var kind = rule.IsMainProgression
            ? UnlockObjectiveKind.MainProgression
            : UnlockObjectiveKind.SectorUnlock;
        return new UnlockObjective(rule.SourcePoint, rule.UnlocksPoint, kind);
    }

    public bool IsPointUnlocked(uint point, UnlockState state) => state.UnlockedPoints.Contains(point);

    public IReadOnlyList<uint> ReserveRoute(IReadOnlyList<uint> route, UnlockState state)
    {
        foreach (var point in route.Where(point => !state.ExploredPoints.Contains(point)))
            state.PendingExplorePoints.Add(point);

        var targets = GetUnlockTargetsForRoute(route, state, rank: int.MaxValue);
        foreach (var target in targets)
        {
            state.PendingUnlockPoints.Add(target);
            state.PendingUnlockAttempts[target] = state.PendingUnlockAttempts.GetValueOrDefault(target) + 1;
        }

        return targets;
    }

    public void ReleaseRouteReservations(IEnumerable<uint> targets, UnlockState state)
    {
        foreach (var target in targets.Distinct())
        {
            var remaining = state.PendingUnlockAttempts.GetValueOrDefault(target) - 1;
            if (remaining > 0)
            {
                state.PendingUnlockAttempts[target] = remaining;
                continue;
            }

            state.PendingUnlockAttempts.Remove(target);
            state.PendingUnlockPoints.Remove(target);
        }
    }

    public IReadOnlyList<uint> MarkRouteUnlocks(
        IReadOnlyList<uint> route,
        UnlockState state,
        int rank,
        long submarineId,
        DateTimeOffset returnAtUtc)
        => MarkRouteReturn(route, state, submarineId, returnAtUtc);

    public IReadOnlyList<uint> MarkRouteReturn(
        IReadOnlyList<uint> route,
        UnlockState state,
        long submarineId,
        DateTimeOffset returnAtUtc)
        => MarkRouteReturn(route, state, submarineId, returnAtUtc, _ => true);

    public IReadOnlyList<uint> MarkRouteReturn(
        IReadOnlyList<uint> route,
        UnlockState state,
        long submarineId,
        DateTimeOffset returnAtUtc,
        Func<UnlockRule, bool> unlockSucceeded)
    {
        var unlocked = new List<uint>();
        foreach (var point in route)
        {
            state.PendingExplorePoints.Remove(point);
            if (!state.ExploredPoints.Add(point))
                continue;

            state.UnlockMilestones.Add(new UnlockMilestone(
                submarineId,
                point,
                point,
                returnAtUtc,
                UnlockMilestoneKind.SectorExplored));

            var metadata = catalog.UnlockRules.FirstOrDefault(rule => rule.UnlocksPoint == point);
            if (metadata?.UnlocksSubSlot == true && state.KnownSubmarineSlots < 4)
            {
                state.KnownSubmarineSlots++;
                state.UnlockMilestones.Add(new UnlockMilestone(
                    submarineId,
                    point,
                    point,
                    returnAtUtc,
                    UnlockMilestoneKind.SubmarineSlotUnlocked));
            }

            if (metadata?.UnlocksMap == true)
            {
                state.UnlockMilestones.Add(new UnlockMilestone(
                    submarineId,
                    point,
                    point,
                    returnAtUtc,
                    UnlockMilestoneKind.MapUnlocked));
            }
        }

        foreach (var sourcePoint in route.Distinct())
        {
            var rule = GetNextLockedRuleForSource(sourcePoint, state, int.MaxValue);
            if (rule is null || !unlockSucceeded(rule))
                continue;

            if (!state.UnlockedPoints.Add(rule.UnlocksPoint))
                continue;

            state.UnlockMilestones.Add(new UnlockMilestone(
                submarineId,
                rule.SourcePoint,
                rule.UnlocksPoint,
                returnAtUtc,
                UnlockMilestoneKind.SectorUnlocked));
            unlocked.Add(rule.UnlocksPoint);
        }

        return unlocked;
    }

    public IReadOnlyList<uint> GetUnlockTargetsForRoute(IReadOnlyList<uint> route, UnlockState state, int rank)
        => route
            .Distinct()
            .Select(source => GetNextLockedRuleForSource(source, state, rank))
            .Where(rule => rule is not null)
            .Select(rule => rule!.UnlocksPoint)
            .ToArray();

    public IReadOnlyList<uint> GetSaturatedUnlockSourcePoints(UnlockState state, EtaSettings settings)
        => catalog.UnlockRules
            .Where(rule => IsTargetSaturated(rule.UnlocksPoint, state, settings))
            .Select(rule => rule.SourcePoint)
            .Distinct()
            .ToArray();

    public int GetDesiredConcurrentAttempts(EtaSettings settings)
    {
        var probability = Math.Clamp(settings.UnlockSuccessProbability, 0.01, 1.0);
        if (probability >= 1.0)
            return 1;

        return Math.Clamp((int)Math.Ceiling(Math.Log(0.5) / Math.Log(1.0 - probability)), 1, 4);
    }

    public UnlockRule? GetNextLockedRuleForSource(uint sourcePoint, UnlockState state, int rank)
        => catalog.UnlockRules
            .Where(rule => rule.SourcePoint == sourcePoint)
            .Where(rule => rule.SourceRequiredRank <= rank)
            .Where(rule => !state.UnlockedPoints.Contains(rule.UnlocksPoint))
            .OrderBy(rule => rule.UnlocksPoint)
            .FirstOrDefault();

    private UnlockObjective? GetSubmarineSlotObjective(UnlockState state, EtaSettings settings, int rank, bool fleetMode)
    {
        foreach (var rule in catalog.UnlockRules.Where(rule => rule.UnlocksSubSlot).OrderBy(rule => rule.UnlocksPoint))
        {
            if (state.UnlockedPoints.Contains(rule.UnlocksPoint) &&
                !state.ExploredPoints.Contains(rule.UnlocksPoint) &&
                catalog.GetPointRequiredRank(rule.UnlocksPoint) <= rank &&
                (!fleetMode || !state.PendingExplorePoints.Contains(rule.UnlocksPoint)))
            {
                return new UnlockObjective(
                    rule.UnlocksPoint,
                    rule.UnlocksPoint,
                    UnlockObjectiveKind.ExploreSubmarineSlot);
            }

            var path = GetUnlockPath(rule.UnlocksPoint);
            foreach (var target in path.Skip(1))
            {
                var pathRule = catalog.UnlockRules.FirstOrDefault(candidate => candidate.UnlocksPoint == target);
                if (pathRule is null ||
                    pathRule.SourceRequiredRank > rank ||
                    !state.UnlockedPoints.Contains(pathRule.SourcePoint) ||
                    state.UnlockedPoints.Contains(pathRule.UnlocksPoint))
                {
                    continue;
                }

                var requiredRule = GetOrderedPrerequisite(pathRule, state, rank);
                if (requiredRule is null)
                    continue;

                // A saturated prerequisite blocks the entire slot path for this fleet event.
                // Remaining submarines should level instead of skipping to a later target.
                if (fleetMode && IsTargetSaturated(requiredRule.UnlocksPoint, state, settings))
                    return null;

                return new UnlockObjective(
                    requiredRule.SourcePoint,
                    requiredRule.UnlocksPoint,
                    UnlockObjectiveKind.ExploreSubmarineSlot);
            }
        }

        return null;
    }

    private IEnumerable<UnlockRule> GetReachableRules(
        UnlockState state,
        EtaSettings settings,
        int rank,
        int targetRank,
        bool fleetMode)
    {
        var goal = settings.GetEffectiveRouteGoal();
        if (goal == RouteGoal.UnlockLevelingRoutesThenLevel)
        {
            foreach (var mainTarget in catalog.UnlockRules
                         .Where(rule => rule.IsMainProgression && rule.TargetRequiredRank <= targetRank)
                         .OrderBy(rule => rule.UnlocksPoint))
            {
                if (state.UnlockedPoints.Contains(mainTarget.UnlocksPoint))
                    continue;

                foreach (var pathTarget in GetUnlockPath(mainTarget.UnlocksPoint).Skip(1))
                {
                    var pathRule = catalog.UnlockRules.FirstOrDefault(rule => rule.UnlocksPoint == pathTarget);
                    if (pathRule is null ||
                        pathRule.SourceRequiredRank > rank ||
                        !state.UnlockedPoints.Contains(pathRule.SourcePoint) ||
                        state.UnlockedPoints.Contains(pathRule.UnlocksPoint))
                    {
                        continue;
                    }

                    var requiredRule = GetOrderedPrerequisite(pathRule, state, rank);
                    if (requiredRule is null)
                        continue;

                    // Do not skip an earlier sibling while enough attempts are already in flight.
                    // Returning no unlock objective lets the route selector choose a leveling route.
                    if (fleetMode && IsTargetSaturated(requiredRule.UnlocksPoint, state, settings))
                        return [];

                    return [requiredRule];
                }

                return [];
            }

            return [];
        }

        return catalog.UnlockRules
            .Where(rule => rule.SourceRequiredRank <= rank)
            .Where(rule => state.UnlockedPoints.Contains(rule.SourcePoint))
            .Where(rule => !state.UnlockedPoints.Contains(rule.UnlocksPoint))
            .Where(rule => GetNextLockedRuleForSource(rule.SourcePoint, state, rank) == rule)
            .Where(rule => !fleetMode || !IsTargetSaturated(rule.UnlocksPoint, state, settings))
            .OrderBy(rule => rule.UnlocksPoint);
    }

    private UnlockRule? GetOrderedPrerequisite(UnlockRule desiredRule, UnlockState state, int rank)
    {
        // A source with multiple targets can only unlock its lowest currently locked target.
        // That target may be an unflagged sibling which must precede the desired main-path or
        // submarine-slot target (for example, 53 -> 54 before 53 -> 55).
        return GetNextLockedRuleForSource(desiredRule.SourcePoint, state, rank);
    }

    private bool IsTargetSaturated(uint targetPoint, UnlockState state, EtaSettings settings)
    {
        var pending = state.PendingUnlockAttempts.TryGetValue(targetPoint, out var count)
            ? count
            : state.PendingUnlockPoints.Contains(targetPoint) ? 1 : 0;
        return pending >= GetDesiredConcurrentAttempts(settings);
    }
}
