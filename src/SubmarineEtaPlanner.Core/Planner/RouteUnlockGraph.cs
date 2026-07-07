namespace SubmarineEtaPlanner.Planner;

public sealed class RouteUnlockGraph(ISubmarineCatalog catalog)
{
    public IReadOnlyList<uint> GetUnlockPath(uint targetPoint)
    {
        var reverse = catalog.UnlockRules.ToDictionary(r => r.UnlocksPoint, r => r.SourcePoint);
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
        var reachable = catalog.UnlockRules
            .Where(r => r.RequiredRank <= rank)
            .Where(r => unlockedPoints.Contains(r.SourcePoint))
            .Where(r => !unlockedPoints.Contains(r.UnlocksPoint))
            .ToList();

        if (prioritizeSubSlots)
        {
            var slotUnlocks = reachable.Where(r => r.UnlocksSubSlot).Select(r => r.SourcePoint).Distinct().ToList();
            if (slotUnlocks.Count > 0)
                return slotUnlocks;
        }

        return reachable.Select(r => r.SourcePoint).Distinct().ToArray();
    }

    public bool IsPointUnlocked(uint point, UnlockState state) => state.UnlockedPoints.Contains(point);

    public IReadOnlyList<uint> MarkRouteUnlocks(
        IReadOnlyList<uint> route,
        UnlockState state,
        int rank,
        long submarineId,
        DateTimeOffset returnAtUtc)
    {
        var unlocked = new List<uint>();
        foreach (var rule in catalog.UnlockRules)
        {
            if (rule.RequiredRank > rank)
                continue;
            if (!route.Contains(rule.SourcePoint))
                continue;
            if (state.UnlockedPoints.Contains(rule.UnlocksPoint))
                continue;

            state.UnlockedPoints.Add(rule.UnlocksPoint);
            state.ExploredPoints.Add(rule.UnlocksPoint);
            state.PendingUnlockPoints.Remove(rule.UnlocksPoint);
            state.UnlockMilestones.Add(new UnlockMilestone(submarineId, rule.SourcePoint, rule.UnlocksPoint, returnAtUtc));
            unlocked.Add(rule.UnlocksPoint);
        }

        return unlocked;
    }

    public IReadOnlyList<uint> GetUnlockTargetsForRoute(IReadOnlyList<uint> route, UnlockState state, int rank)
        => catalog.UnlockRules
            .Where(r => r.RequiredRank <= rank)
            .Where(r => route.Contains(r.SourcePoint))
            .Where(r => !state.UnlockedPoints.Contains(r.UnlocksPoint))
            .Select(r => r.UnlocksPoint)
            .Distinct()
            .ToArray();
}
