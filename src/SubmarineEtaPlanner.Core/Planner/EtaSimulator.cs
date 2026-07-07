namespace SubmarineEtaPlanner.Planner;

public sealed class EtaSimulator(
    BuildResolver buildResolver,
    RouteUnlockGraph unlockGraph,
    RouteSelector routeSelector,
    ISubmarineCatalog catalog)
{
    public EtaResult Simulate(FcState fc, EtaSettings settings, DateTimeOffset now)
        => Simulate(fc, settings, now, null);

    public EtaResult Simulate(FcState fc, EtaSettings settings, DateTimeOffset now, DateTimeOffset? deadlineUtc)
        => settings.SimulationMode switch
        {
            SimulationMode.OptimisticPerSub => SimulateOptimistic(fc, settings, now, deadlineUtc),
            _ => SimulateFleet(fc, settings, now, deadlineUtc),
        };

    private EtaResult SimulateOptimistic(FcState fc, EtaSettings settings, DateTimeOffset now, DateTimeOffset? deadlineUtc)
    {
        var results = new List<PerSubEtaResult>();
        var allPlans = new List<VoyagePlan>();
        var warnings = new List<string>();

        foreach (var sub in fc.Submarines)
        {
            if (IsTimedOut(deadlineUtc))
            {
                warnings.Add($"Calculation time limit reached while simulating {fc.DisplayName}; results are partial.");
                break;
            }

            var unlockState = CreateUnlockState(fc);
            var result = SimulateSingleSub(sub, fc, unlockState, settings, now, fleetMode: false, deadlineUtc);
            results.Add(result);
            allPlans.AddRange(result.VoyagePreview);
            warnings.AddRange(result.Warnings);
        }

        return CreateEtaResult(fc, settings, now, results, allPlans, results.SelectMany(r => r.UnlockMilestones), warnings);
    }

    private EtaResult SimulateFleet(FcState fc, EtaSettings settings, DateTimeOffset now, DateTimeOffset? deadlineUtc)
    {
        var unlockState = CreateUnlockState(fc);
        var states = fc.Submarines.ToDictionary(
            s => s.SubmarineId,
            s => new MutableSubState(s, GetStartingAvailableTime(s, settings, now), s.Rank, s.CurrentExp, s.NextLevelExp));

        var queue = new PriorityQueue<long, DateTimeOffset>();
        foreach (var state in states.Values)
            queue.Enqueue(state.Source.SubmarineId, state.NextAvailableAt);

        var plans = new List<VoyagePlan>();
        var warnings = new List<string>();
        var finished = new HashSet<long>();
        var perSubPlans = states.Keys.ToDictionary(id => id, _ => new List<VoyagePlan>());
        var perSubWarnings = states.Keys.ToDictionary(id => id, _ => new List<string>());

        while (queue.Count > 0 && finished.Count < states.Count)
        {
            if (IsTimedOut(deadlineUtc))
            {
                warnings.Add($"Calculation time limit reached while simulating {fc.DisplayName}; results are partial.");
                break;
            }

            var submarineId = queue.Dequeue();
            var mutable = states[submarineId];
            if (finished.Contains(submarineId))
                continue;

            if (!mutable.Source.CurrentVoyageKnown && mutable.Source.ReturnAtUtc > now && perSubPlans[submarineId].Count == 0)
            {
                var warning = $"Current voyage route is unknown for {mutable.Source.Name}.";
                warnings.Add(warning);
                perSubWarnings[submarineId].Add(warning);
                if (settings.UnknownCurrentVoyagePolicy == UnknownCurrentVoyagePolicy.BlockSimulation)
                {
                    finished.Add(submarineId);
                    continue;
                }
            }

            if (mutable.Rank >= settings.TargetRank)
            {
                finished.Add(submarineId);
                continue;
            }

            if (perSubPlans[submarineId].Count >= settings.SimulationSafetyVoyageCapPerSubmarine)
            {
                var warning = $"Simulation stopped for {mutable.Source.Name} after {settings.SimulationSafetyVoyageCapPerSubmarine} voyages.";
                warnings.Add(warning);
                perSubWarnings[submarineId].Add(warning);
                finished.Add(submarineId);
                continue;
            }

            var build = buildResolver.ResolveBuildForRank(mutable.Rank, settings);
            var route = routeSelector.SelectNextRoute(mutable.Source, unlockState, build, settings, fleetMode: true, deadlineUtc);
            if (route.Route.Count == 0 || route.Exp == 0)
            {
                var warning = $"No valid route found for {mutable.Source.Name}; ETA may be incomplete.";
                warnings.Add(warning);
                perSubWarnings[submarineId].Add(warning);
                finished.Add(submarineId);
                continue;
            }

            var departAt = mutable.NextAvailableAt;
            var returnAt = departAt + route.Duration + TimeSpan.FromMinutes(settings.CollectionDelayMinutes);
            var beforeRank = mutable.Rank;
            var beforeExp = mutable.CurrentExp;
            var rankResult = catalog.ApplyExp(mutable.Rank, mutable.CurrentExp, route.Exp, settings.TargetRank);
            mutable.Rank = rankResult.Rank;
            mutable.CurrentExp = rankResult.CurrentExp;
            mutable.NextLevelExp = rankResult.NextLevelExp;
            mutable.NextAvailableAt = returnAt;

            var unlocks = unlockGraph.MarkRouteUnlocks(route.Route, unlockState, mutable.Rank, submarineId, returnAt);
            var plan = new VoyagePlan(
                submarineId,
                mutable.Source.Name,
                departAt,
                returnAt,
                build.Code,
                route.Route,
                route.Exp,
                beforeRank,
                mutable.Rank,
                beforeExp,
                mutable.CurrentExp,
                unlocks,
                []);

            plans.Add(plan);
            perSubPlans[submarineId].Add(plan);

            if (mutable.Rank >= settings.TargetRank)
                finished.Add(submarineId);
            else
                queue.Enqueue(submarineId, mutable.NextAvailableAt);
        }

        var results = states.Values.Select(state =>
        {
            var subPlans = perSubPlans[state.Source.SubmarineId];
            var etaAt = subPlans.LastOrDefault()?.ReturnAtUtc ?? GetStartingAvailableTime(state.Source, settings, now);
            var build = buildResolver.GetBuildCodeForRank(Math.Max(state.Rank, settings.TargetRank), settings);
            return new PerSubEtaResult(
                state.Source.SubmarineId,
                state.Source.Name,
                state.Source.Rank,
                state.Rank,
                etaAt,
                etaAt - now,
                subPlans.Count,
                build,
                subPlans.LastOrDefault()?.Route ?? [],
                subPlans.Take(settings.MaxPreviewVoyagesPerSubmarine).ToArray(),
                unlockState.UnlockMilestones.Where(m => m.SubmarineId == state.Source.SubmarineId).ToArray(),
                perSubWarnings[state.Source.SubmarineId],
                catalog.IsPostTargetFarmingReady(buildResolver.ResolveBuildForRank(settings.TargetRank, settings), unlockState.UnlockedPoints));
        }).ToArray();

        return CreateEtaResult(fc, settings, now, results, plans, unlockState.UnlockMilestones, warnings);
    }

    private PerSubEtaResult SimulateSingleSub(
        SubmarineState sub,
        FcState fc,
        UnlockState unlockState,
        EtaSettings settings,
        DateTimeOffset now,
        bool fleetMode,
        DateTimeOffset? deadlineUtc)
    {
        var warnings = new List<string>();
        var plans = new List<VoyagePlan>();
        var rank = sub.Rank;
        var currentExp = sub.CurrentExp;
        var nextLevelExp = sub.NextLevelExp;
        var nextAvailable = GetStartingAvailableTime(sub, settings, now);

        if (!sub.CurrentVoyageKnown && sub.ReturnAtUtc > now)
        {
            var warning = $"Current voyage route is unknown for {sub.Name}.";
            warnings.Add(warning);
            if (settings.UnknownCurrentVoyagePolicy == UnknownCurrentVoyagePolicy.BlockSimulation)
            {
                return new PerSubEtaResult(
                    sub.SubmarineId,
                    sub.Name,
                    sub.Rank,
                    rank,
                    nextAvailable,
                    nextAvailable - now,
                    0,
                    buildResolver.GetBuildCodeForRank(rank, settings),
                    [],
                    [],
                    [],
                    warnings,
                    false);
            }
        }

        while (rank < settings.TargetRank && plans.Count < settings.SimulationSafetyVoyageCapPerSubmarine)
        {
            if (IsTimedOut(deadlineUtc))
            {
                warnings.Add($"Calculation time limit reached for {sub.Name}; ETA is partial.");
                break;
            }

            var build = buildResolver.ResolveBuildForRank(rank, settings);
            var tempSub = sub with { Rank = rank, CurrentExp = currentExp, NextLevelExp = nextLevelExp };
            var route = routeSelector.SelectNextRoute(tempSub, unlockState, build, settings, fleetMode, deadlineUtc);
            if (route.Route.Count == 0 || route.Exp == 0)
            {
                warnings.Add($"No valid route found for {sub.Name}; ETA may be incomplete.");
                break;
            }

            var departAt = nextAvailable;
            var returnAt = departAt + route.Duration + TimeSpan.FromMinutes(settings.CollectionDelayMinutes);
            var beforeRank = rank;
            var beforeExp = currentExp;
            var rankResult = catalog.ApplyExp(rank, currentExp, route.Exp, settings.TargetRank);
            rank = rankResult.Rank;
            currentExp = rankResult.CurrentExp;
            nextLevelExp = rankResult.NextLevelExp;
            nextAvailable = returnAt;

            var unlocks = unlockGraph.MarkRouteUnlocks(route.Route, unlockState, rank, sub.SubmarineId, returnAt);
            plans.Add(new VoyagePlan(
                sub.SubmarineId,
                sub.Name,
                departAt,
                returnAt,
                build.Code,
                route.Route,
                route.Exp,
                beforeRank,
                rank,
                beforeExp,
                currentExp,
                unlocks,
                []));
        }

        if (plans.Count >= settings.SimulationSafetyVoyageCapPerSubmarine)
            warnings.Add($"Simulation stopped for {sub.Name} after {settings.SimulationSafetyVoyageCapPerSubmarine} voyages.");

        return new PerSubEtaResult(
            sub.SubmarineId,
            sub.Name,
            sub.Rank,
            rank,
            plans.LastOrDefault()?.ReturnAtUtc ?? nextAvailable,
            (plans.LastOrDefault()?.ReturnAtUtc ?? nextAvailable) - now,
            plans.Count,
            buildResolver.GetBuildCodeForRank(Math.Max(rank, settings.TargetRank), settings),
            plans.LastOrDefault()?.Route ?? [],
            plans.Take(settings.MaxPreviewVoyagesPerSubmarine).ToArray(),
            unlockState.UnlockMilestones.Where(m => m.SubmarineId == sub.SubmarineId).ToArray(),
            warnings,
            catalog.IsPostTargetFarmingReady(buildResolver.ResolveBuildForRank(settings.TargetRank, settings), unlockState.UnlockedPoints));
    }

    private static UnlockState CreateUnlockState(FcState fc) => new(
        new HashSet<uint>(fc.UnlockedPoints),
        new HashSet<uint>(fc.ExploredPoints),
        [],
        []);

    private static DateTimeOffset GetStartingAvailableTime(SubmarineState sub, EtaSettings settings, DateTimeOffset now)
    {
        var availableAt = sub.ReturnAtUtc > now ? sub.ReturnAtUtc : now;
        return availableAt + TimeSpan.FromMinutes(settings.CollectionDelayMinutes);
    }

    private static bool IsTimedOut(DateTimeOffset? deadlineUtc)
        => deadlineUtc is not null && DateTimeOffset.UtcNow >= deadlineUtc.Value;

    private static EtaResult CreateEtaResult(
        FcState fc,
        EtaSettings settings,
        DateTimeOffset now,
        IReadOnlyList<PerSubEtaResult> results,
        IEnumerable<VoyagePlan> plans,
        IEnumerable<UnlockMilestone> milestones,
        IEnumerable<string> warnings)
    {
        var resultArray = results.ToArray();
        var completion = resultArray.Length == 0 ? now : resultArray.Max(r => r.EtaAtUtc);
        var planArray = plans.ToArray();
        return new EtaResult(
            fc.FcId,
            fc.DisplayName,
            now,
            settings.TargetRank,
            settings.SimulationMode,
            resultArray,
            completion,
            planArray.Length,
            planArray,
            milestones.ToArray(),
            warnings.Distinct().ToArray());
    }

    private sealed class MutableSubState(
        SubmarineState source,
        DateTimeOffset nextAvailableAt,
        int rank,
        uint currentExp,
        uint nextLevelExp)
    {
        public SubmarineState Source { get; } = source;

        public DateTimeOffset NextAvailableAt { get; set; } = nextAvailableAt;

        public int Rank { get; set; } = rank;

        public uint CurrentExp { get; set; } = currentExp;

        public uint NextLevelExp { get; set; } = nextLevelExp;
    }
}
