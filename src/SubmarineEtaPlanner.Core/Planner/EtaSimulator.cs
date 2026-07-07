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
            var result = SimulateSingleSub(sub, unlockState, settings, now, fleetMode: false, deadlineUtc);
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

            if (!mutable.CurrentVoyageApplied && !mutable.Source.CurrentVoyageKnown && mutable.Source.ReturnAtUtc > now)
            {
                var warning = $"Current voyage route is unknown for {mutable.Source.Name}.";
                warnings.Add(warning);
                perSubWarnings[submarineId].Add(warning);
                mutable.CurrentVoyageApplied = true;
                if (settings.UnknownCurrentVoyagePolicy == UnknownCurrentVoyagePolicy.BlockSimulation)
                {
                    finished.Add(submarineId);
                    continue;
                }
            }

            if (!mutable.CurrentVoyageApplied)
            {
                var currentVoyage = ApplyCurrentVoyageIfKnown(
                    mutable.Source,
                    unlockState,
                    settings,
                    now,
                    mutable.Rank,
                    mutable.CurrentExp,
                    mutable.NextLevelExp,
                    mutable.NextAvailableAt);
                mutable.Rank = currentVoyage.Rank;
                mutable.CurrentExp = currentVoyage.CurrentExp;
                mutable.NextLevelExp = currentVoyage.NextLevelExp;
                mutable.NextAvailableAt = currentVoyage.NextAvailableAt;
                mutable.CurrentVoyageApplied = true;
            }

            if (mutable.Rank >= settings.TargetRank)
            {
                finished.Add(submarineId);
                continue;
            }

            if (mutable.VoyageCount >= settings.SimulationSafetyVoyageCapPerSubmarine)
            {
                var warning = $"Simulation stopped for {mutable.Source.Name} after {settings.SimulationSafetyVoyageCapPerSubmarine} voyages.";
                warnings.Add(warning);
                perSubWarnings[submarineId].Add(warning);
                finished.Add(submarineId);
                continue;
            }

            var build = buildResolver.ResolveBuildForRank(mutable.Rank, settings);
            var currentSub = mutable.Source with
            {
                Rank = mutable.Rank,
                CurrentExp = mutable.CurrentExp,
                NextLevelExp = mutable.NextLevelExp,
            };
            var route = routeSelector.SelectNextRoute(currentSub, unlockState, build, settings, fleetMode: true, deadlineUtc);
            if (route.Route.Count == 0 || route.Exp == 0)
            {
                var warning = $"No valid route found for {mutable.Source.Name}; ETA is incomplete.";
                warnings.Add(warning);
                perSubWarnings[submarineId].Add(warning);
                finished.Add(submarineId);
                continue;
            }

            var batchCount = CalculateBatchCount(route, unlockState, settings, mutable.Rank, mutable.CurrentExp, mutable.NextLevelExp, mutable.VoyageCount, fleetMode: true);
            var plan = ApplyFutureVoyageBatch(
                mutable.Source.Name,
                mutable.Source.SubmarineId,
                settings,
                unlockState,
                route,
                build.Code,
                submarineId,
                mutable.NextAvailableAt,
                mutable.Rank,
                mutable.CurrentExp,
                batchCount);

            mutable.Rank = plan.RankAfter;
            mutable.CurrentExp = plan.ExpAfter;
            mutable.NextLevelExp = mutable.Rank >= settings.TargetRank ? 0 : catalog.ApplyExp(mutable.Rank, mutable.CurrentExp, 0, settings.TargetRank).NextLevelExp;
            mutable.NextAvailableAt = plan.ReturnAtUtc;
            mutable.VoyageCount += batchCount;

            if (perSubPlans[submarineId].Count < settings.MaxPreviewVoyagesPerSubmarine)
                perSubPlans[submarineId].Add(plan);
            plans.Add(plan);

            if (mutable.Rank >= settings.TargetRank)
                finished.Add(submarineId);
            else
                queue.Enqueue(submarineId, mutable.NextAvailableAt);
        }

        var results = states.Values.Select(state =>
        {
            var subPlans = perSubPlans[state.Source.SubmarineId];
            var firstPlan = subPlans.FirstOrDefault();
            var etaAt = state.NextAvailableAt;
            var build = firstPlan?.BuildCode ?? buildResolver.GetBuildCodeForRank(state.Source.Rank, settings);
            var subWarnings = perSubWarnings[state.Source.SubmarineId].ToList();
            var status = state.Rank >= settings.TargetRank && subWarnings.All(w => !IsIncompleteWarning(w))
                ? CalculationStatus.Complete
                : CalculationStatus.Partial;
            var reason = status == CalculationStatus.Complete
                ? null
                : CreateIncompleteReason(state.Source.Name, state.Rank, settings.TargetRank, subWarnings);

            if (status != CalculationStatus.Complete && reason is not null && !subWarnings.Contains(reason))
                subWarnings.Add(reason);

            return new PerSubEtaResult(
                state.Source.SubmarineId,
                state.Source.Name,
                state.Source.Rank,
                state.Rank,
                etaAt,
                etaAt - now,
                state.VoyageCount,
                build,
                firstPlan?.Route ?? [],
                subPlans.ToArray(),
                unlockState.UnlockMilestones.Where(m => m.SubmarineId == state.Source.SubmarineId).ToArray(),
                subWarnings,
                catalog.IsPostTargetFarmingReady(buildResolver.ResolveBuildForRank(settings.TargetRank, settings), unlockState.UnlockedPoints),
                status,
                reason);
        }).ToArray();

        return CreateEtaResult(fc, settings, now, results, plans, unlockState.UnlockMilestones, warnings);
    }

    private PerSubEtaResult SimulateSingleSub(
        SubmarineState sub,
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
        var voyageCount = 0;

        if (!sub.CurrentVoyageKnown && sub.ReturnAtUtc > now)
        {
            var warning = $"Current voyage route is unknown for {sub.Name}.";
            warnings.Add(warning);
            if (settings.UnknownCurrentVoyagePolicy == UnknownCurrentVoyagePolicy.BlockSimulation)
            {
                return CreatePerSubResult(
                    sub,
                    settings,
                    now,
                    rank,
                    nextAvailable,
                    voyageCount,
                    plans,
                    unlockState,
                    warnings,
                    forcedPartialReason: warning);
            }
        }

        var appliedCurrentVoyage = ApplyCurrentVoyageIfKnown(
            sub,
            unlockState,
            settings,
            now,
            rank,
            currentExp,
            nextLevelExp,
            nextAvailable);
        rank = appliedCurrentVoyage.Rank;
        currentExp = appliedCurrentVoyage.CurrentExp;
        nextLevelExp = appliedCurrentVoyage.NextLevelExp;
        nextAvailable = appliedCurrentVoyage.NextAvailableAt;

        while (rank < settings.TargetRank && voyageCount < settings.SimulationSafetyVoyageCapPerSubmarine)
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
                warnings.Add($"No valid route found for {sub.Name}; ETA is incomplete.");
                break;
            }

            var batchCount = CalculateBatchCount(route, unlockState, settings, rank, currentExp, nextLevelExp, voyageCount, fleetMode);
            var plan = ApplyFutureVoyageBatch(
                sub.Name,
                sub.SubmarineId,
                settings,
                unlockState,
                route,
                build.Code,
                sub.SubmarineId,
                nextAvailable,
                rank,
                currentExp,
                batchCount);

            rank = plan.RankAfter;
            currentExp = plan.ExpAfter;
            nextLevelExp = rank >= settings.TargetRank ? 0 : catalog.ApplyExp(rank, currentExp, 0, settings.TargetRank).NextLevelExp;
            nextAvailable = plan.ReturnAtUtc;
            voyageCount += batchCount;

            if (plans.Count < settings.MaxPreviewVoyagesPerSubmarine)
                plans.Add(plan);
        }

        if (voyageCount >= settings.SimulationSafetyVoyageCapPerSubmarine && rank < settings.TargetRank)
            warnings.Add($"Simulation stopped for {sub.Name} after {settings.SimulationSafetyVoyageCapPerSubmarine} voyages.");

        return CreatePerSubResult(sub, settings, now, rank, nextAvailable, voyageCount, plans, unlockState, warnings);
    }

    private VoyagePlan ApplyFutureVoyageBatch(
        string submarineName,
        long submarineId,
        EtaSettings settings,
        UnlockState unlockState,
        RouteCandidate route,
        string buildCode,
        long unlockSubmarineId,
        DateTimeOffset departAt,
        int rank,
        uint currentExp,
        int batchCount)
    {
        var perVoyageDuration = route.Duration + TimeSpan.FromMinutes(settings.CollectionDelayMinutes);
        var returnAt = departAt + TimeSpan.FromTicks(perVoyageDuration.Ticks * batchCount);
        var gainedExp = checked((uint)Math.Min((ulong)uint.MaxValue, (ulong)route.Exp * (ulong)batchCount));
        var rankResult = catalog.ApplyExp(rank, currentExp, gainedExp, settings.TargetRank);
        IReadOnlyList<uint> unlocks = batchCount == 1
            ? unlockGraph.MarkRouteUnlocks(route.Route, unlockState, rankResult.Rank, unlockSubmarineId, returnAt)
            : Array.Empty<uint>();
        IReadOnlyList<string> warnings = batchCount > 1
            ? [$"Batched {batchCount} identical voyages."]
            : Array.Empty<string>();

        return new VoyagePlan(
            submarineId,
            submarineName,
            departAt,
            returnAt,
            buildCode,
            route.Route,
            gainedExp,
            rank,
            rankResult.Rank,
            currentExp,
            rankResult.CurrentExp,
            unlocks,
            warnings,
            TimeSpan.FromTicks(perVoyageDuration.Ticks * batchCount),
            route.ExpPerHour,
            route.EtaModel,
            route.DurationCapApplied);
    }

    private int CalculateBatchCount(
        RouteCandidate route,
        UnlockState unlockState,
        EtaSettings settings,
        int rank,
        uint currentExp,
        uint nextLevelExp,
        int voyageCount,
        bool fleetMode)
    {
        if (!CanBatch(route, unlockState, settings, fleetMode))
            return 1;

        var remainingCap = settings.SimulationSafetyVoyageCapPerSubmarine - voyageCount;
        if (remainingCap <= 1)
            return 1;

        var expNeeded = nextLevelExp > currentExp ? nextLevelExp - currentExp : nextLevelExp;
        if (expNeeded == 0 || rank >= settings.TargetRank)
            return 1;

        var voyagesToNextRank = (int)Math.Ceiling(expNeeded / (double)Math.Max(route.Exp, 1));
        return Math.Clamp(voyagesToNextRank, 1, remainingCap);
    }

    private bool CanBatch(RouteCandidate route, UnlockState unlockState, EtaSettings settings, bool fleetMode)
    {
        if (settings.EtaModel != EtaModel.PracticalLeveling || settings.EffectiveRouteGoal != RouteGoal.FastestLevelingOnly)
            return false;
        if (route.UnlockTargets.Count > 0)
            return false;

        return !fleetMode || catalog.UnlockRules.All(rule => unlockState.UnlockedPoints.Contains(rule.UnlocksPoint));
    }

    private PerSubEtaResult CreatePerSubResult(
        SubmarineState sub,
        EtaSettings settings,
        DateTimeOffset now,
        int rank,
        DateTimeOffset etaAt,
        int voyageCount,
        IReadOnlyList<VoyagePlan> plans,
        UnlockState unlockState,
        IReadOnlyList<string> warnings,
        string? forcedPartialReason = null)
    {
        var firstPlan = plans.FirstOrDefault();
        var status = rank >= settings.TargetRank && forcedPartialReason is null && warnings.All(w => !IsIncompleteWarning(w))
            ? CalculationStatus.Complete
            : CalculationStatus.Partial;
        var reason = status == CalculationStatus.Complete
            ? null
            : forcedPartialReason ?? CreateIncompleteReason(sub.Name, rank, settings.TargetRank, warnings);
        var finalWarnings = warnings.ToList();
        if (status != CalculationStatus.Complete && reason is not null && !finalWarnings.Contains(reason))
            finalWarnings.Add(reason);

        return new PerSubEtaResult(
            sub.SubmarineId,
            sub.Name,
            sub.Rank,
            rank,
            etaAt,
            etaAt - now,
            voyageCount,
            firstPlan?.BuildCode ?? buildResolver.GetBuildCodeForRank(sub.Rank, settings),
            firstPlan?.Route ?? [],
            plans.Take(settings.MaxPreviewVoyagesPerSubmarine).ToArray(),
            unlockState.UnlockMilestones.Where(m => m.SubmarineId == sub.SubmarineId).ToArray(),
            finalWarnings,
            catalog.IsPostTargetFarmingReady(buildResolver.ResolveBuildForRank(settings.TargetRank, settings), unlockState.UnlockedPoints),
            status,
            reason);
    }

    private CurrentVoyageApplication ApplyCurrentVoyageIfKnown(
        SubmarineState sub,
        UnlockState unlockState,
        EtaSettings settings,
        DateTimeOffset now,
        int rank,
        uint currentExp,
        uint nextLevelExp,
        DateTimeOffset nextAvailable)
    {
        if (sub.ReturnAtUtc <= now)
            return new CurrentVoyageApplication(rank, currentExp, nextLevelExp, nextAvailable);

        var route = sub.ManualCurrentRouteOverride.Count > 0 ? sub.ManualCurrentRouteOverride : sub.CurrentRoute;
        if (route.Count == 0)
            return new CurrentVoyageApplication(rank, currentExp, nextLevelExp, nextAvailable);

        var build = catalog.ResolveBuild(sub.BuildParts, rank) ?? buildResolver.ResolveBuildForRank(rank, settings);
        var exp = catalog.CalculateExp(route, build, settings.EffectiveExpMode);
        var rankResult = catalog.ApplyExp(rank, currentExp, exp, settings.TargetRank);
        var returnAt = sub.ReturnAtUtc + TimeSpan.FromMinutes(settings.CollectionDelayMinutes);
        unlockGraph.MarkRouteUnlocks(route, unlockState, rankResult.Rank, sub.SubmarineId, returnAt);

        return new CurrentVoyageApplication(rankResult.Rank, rankResult.CurrentExp, rankResult.NextLevelExp, returnAt);
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

    private static bool IsIncompleteWarning(string warning)
        => warning.Contains("partial", StringComparison.OrdinalIgnoreCase) ||
           warning.Contains("incomplete", StringComparison.OrdinalIgnoreCase) ||
           warning.Contains("stopped", StringComparison.OrdinalIgnoreCase) ||
           warning.Contains("No valid route", StringComparison.OrdinalIgnoreCase);

    private static string CreateIncompleteReason(
        string submarineName,
        int finalRank,
        int targetRank,
        IReadOnlyList<string> warnings)
    {
        var warning = warnings.FirstOrDefault(IsIncompleteWarning);
        return warning ?? $"{submarineName} reached rank {finalRank}, below target rank {targetRank}.";
    }

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
        var warningArray = warnings.Distinct().ToArray();
        var status = resultArray.Length == fc.Submarines.Count && resultArray.All(r => r.IsComplete)
            ? CalculationStatus.Complete
            : CalculationStatus.Partial;
        var reason = status == CalculationStatus.Complete
            ? null
            : resultArray.FirstOrDefault(r => !r.IsComplete)?.IncompleteReason ??
              warningArray.FirstOrDefault(IsIncompleteWarning) ??
              "Calculation stopped before all submarines reached the target rank.";

        return new EtaResult(
            fc.FcId,
            fc.DisplayName,
            now,
            settings.TargetRank,
            settings.SimulationMode,
            resultArray,
            completion,
            resultArray.Sum(r => r.VoyageCount),
            planArray,
            milestones.ToArray(),
            warningArray,
            status,
            reason);
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

        public int VoyageCount { get; set; }

        public bool CurrentVoyageApplied { get; set; }
    }

    private sealed record CurrentVoyageApplication(
        int Rank,
        uint CurrentExp,
        uint NextLevelExp,
        DateTimeOffset NextAvailableAt);
}
