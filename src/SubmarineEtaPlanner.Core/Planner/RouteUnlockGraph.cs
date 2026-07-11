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
            var slotObjective = GetSubmarineSlotObjective(state, rank, fleetMode);
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
            state.PendingUnlockPoints.Add(target);

        return targets;
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

        foreach (var rule in catalog.UnlockRules.Where(rule => route.Contains(rule.SourcePoint)))
        {
            state.PendingUnlockPoints.Remove(rule.UnlocksPoint);
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
        => catalog.UnlockRules
            .Where(rule => route.Contains(rule.SourcePoint))
            .Where(rule => !state.UnlockedPoints.Contains(rule.UnlocksPoint))
            .Select(rule => rule.UnlocksPoint)
            .Distinct()
            .ToArray();

    public IReadOnlyList<uint> GetPendingUnlockSourcePoints(UnlockState state)
        => catalog.UnlockRules
            .Where(rule => state.PendingUnlockPoints.Contains(rule.UnlocksPoint))
            .Select(rule => rule.SourcePoint)
            .Distinct()
            .ToArray();

    private UnlockObjective? GetSubmarineSlotObjective(UnlockState state, int rank, bool fleetMode)
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
                    state.UnlockedPoints.Contains(pathRule.UnlocksPoint) ||
                    (fleetMode && state.PendingUnlockPoints.Contains(pathRule.UnlocksPoint)))
                {
                    continue;
                }

                return new UnlockObjective(
                    pathRule.SourcePoint,
                    pathRule.UnlocksPoint,
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
                        state.UnlockedPoints.Contains(pathRule.UnlocksPoint) ||
                        (fleetMode && state.PendingUnlockPoints.Contains(pathRule.UnlocksPoint)))
                    {
                        continue;
                    }

                    return [pathRule];
                }

                return [];
            }

            return [];
        }

        return catalog.UnlockRules
            .Where(rule => rule.SourceRequiredRank <= rank)
            .Where(rule => state.UnlockedPoints.Contains(rule.SourcePoint))
            .Where(rule => !state.UnlockedPoints.Contains(rule.UnlocksPoint))
            .Where(rule => !fleetMode || !state.PendingUnlockPoints.Contains(rule.UnlocksPoint))
            .OrderBy(rule => rule.UnlocksPoint);
    }
}
