namespace SubmarineEtaPlanner.Planner;

public sealed class EtaSimulator(
    BuildResolver buildResolver,
    RouteUnlockGraph unlockGraph,
    RouteSelector routeSelector,
    ISubmarineCatalog catalog)
{
    public EtaResult Simulate(FcState fc, EtaSettings settings, DateTimeOffset now)
        => Simulate(fc, settings, now, null, CancellationToken.None);

    public EtaResult Simulate(FcState fc, EtaSettings settings, DateTimeOffset now, DateTimeOffset? deadlineUtc)
        => Simulate(fc, settings, now, deadlineUtc, CancellationToken.None);

    public EtaResult Simulate(
        FcState fc,
        EtaSettings settings,
        DateTimeOffset now,
        DateTimeOffset? deadlineUtc,
        CancellationToken cancellationToken)
        => settings.SimulationMode switch
        {
            SimulationMode.OptimisticPerSub => SimulateOptimistic(fc, settings, now, deadlineUtc, cancellationToken),
            _ => SimulateFleet(fc, settings, now, deadlineUtc, cancellationToken),
        };

    private EtaResult SimulateOptimistic(
        FcState fc,
        EtaSettings settings,
        DateTimeOffset now,
        DateTimeOffset? deadlineUtc,
        CancellationToken cancellationToken)
    {
        var results = new List<PerSubEtaResult>();
        var allPlans = new List<VoyagePlan>();
        var warnings = new List<string>();

        foreach (var sub in fc.Submarines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsTimedOut(deadlineUtc))
            {
                warnings.Add($"Calculation time limit reached while simulating {fc.DisplayName}; results are partial.");
                break;
            }

            var unlockState = CreateUnlockState(fc);
            var result = SimulateSingleSub(sub, unlockState, settings, now, fleetMode: false, deadlineUtc, cancellationToken);
            results.Add(result);
            allPlans.AddRange(result.VoyagePreview);
            warnings.AddRange(result.Warnings);
        }

        return CreateEtaResult(fc, settings, now, results, allPlans, results.SelectMany(r => r.UnlockMilestones), warnings);
    }

    private EtaResult SimulateFleet(
        FcState fc,
        EtaSettings settings,
        DateTimeOffset now,
        DateTimeOffset? deadlineUtc,
        CancellationToken cancellationToken)
    {
        var unlockState = CreateUnlockState(fc);
        var states = fc.Submarines.ToDictionary(
            s => s.SubmarineId,
            s => new MutableSubState(
                s,
                s.Rank >= settings.TargetRank ? now : GetStartingAvailableTime(s, settings, now),
                s.Rank,
                s.CurrentExp,
                s.NextLevelExp));

        var queue = new PriorityQueue<long, DateTimeOffset>();
        var plans = new List<VoyagePlan>();
        var warnings = new List<string>();
        var finished = new HashSet<long>();
        var perSubPlans = states.Keys.ToDictionary(id => id, _ => new List<VoyagePlan>());
        var perSubWarnings = states.Keys.ToDictionary(id => id, _ => new List<string>());

        foreach (var state in states.Values)
        {
            if (state.Rank >= settings.TargetRank)
            {
                state.CurrentVoyageApplied = true;
                finished.Add(state.Source.SubmarineId);
                continue;
            }

            if (state.Source.ReturnAtUtc > now && state.Source.CurrentVoyageKnown)
            {
                var currentRoute = state.Source.ManualCurrentRouteOverride.Count > 0
                    ? state.Source.ManualCurrentRouteOverride
                    : state.Source.CurrentRoute;
                if (currentRoute.Count > 0)
                {
                    var currentBuild = catalog.ResolveBuild(state.Source.BuildParts, state.Rank) ??
                                       buildResolver.ResolveBuildForRank(state.Rank, settings);
                    var currentReturn = state.Source.ReturnAtUtc + TimeSpan.FromMinutes(settings.CollectionDelayMinutes);
                    state.PendingVoyage = new PendingVoyage(
                        currentRoute,
                        currentBuild.Code,
                        catalog.CalculateExp(currentRoute, currentBuild, settings.GetEffectiveExpMode()),
                        now,
                        currentReturn,
                        state.Source.ReturnAtUtc - now,
                        1,
                        settings.EtaModel,
                        DurationCapApplied: false,
                        IsCurrentVoyage: true);
                    state.NextAvailableAt = currentReturn;
                    state.CurrentVoyageApplied = true;
                    unlockGraph.ReserveRoute(currentRoute, unlockState);
                }
            }

            if (state.Source.ReturnAtUtc <= now)
                state.CurrentVoyageApplied = true;

            queue.Enqueue(state.Source.SubmarineId, state.NextAvailableAt);
        }

        while (queue.Count > 0 && finished.Count < states.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsTimedOut(deadlineUtc))
            {
                warnings.Add($"Calculation time limit reached while simulating {fc.DisplayName}; results are partial.");
                break;
            }

            queue.TryDequeue(out var firstSubmarineId, out var eventAt);
            var dueSubmarines = new List<long> { firstSubmarineId };
            while (queue.TryPeek(out var nextSubmarineId, out var nextEventAt) && nextEventAt == eventAt)
            {
                queue.Dequeue();
                dueSubmarines.Add(nextSubmarineId);
            }

            var readyToDispatch = new List<long>();
            foreach (var submarineId in dueSubmarines)
            {
                var mutable = states[submarineId];
                if (finished.Contains(submarineId))
                    continue;

                if (mutable.PendingVoyage is not null)
                {
                    var completedPlan = CompletePendingVoyage(mutable, unlockState, settings);
                    mutable.PendingVoyage = null;
                    if (completedPlan is not null)
                    {
                        mutable.VoyageCount += completedPlan.RepeatCount;
                        perSubPlans[submarineId].Add(completedPlan);
                        plans.Add(completedPlan);
                    }
                }

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

                if (mutable.Rank >= settings.TargetRank)
                {
                    finished.Add(submarineId);
                    continue;
                }

                readyToDispatch.Add(submarineId);
            }

            foreach (var submarineId in readyToDispatch)
            {
                var mutable = states[submarineId];
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
                var route = routeSelector.SelectNextRoute(
                    currentSub,
                    unlockState,
                    build,
                    settings,
                    fleetMode: true,
                    deadlineUtc,
                    cancellationToken);
                if (route.Route.Count == 0 || route.Exp == 0)
                {
                    var warning = $"No valid route found for {mutable.Source.Name}; ETA is incomplete.";
                    warnings.Add(warning);
                    perSubWarnings[submarineId].Add(warning);
                    finished.Add(submarineId);
                    continue;
                }

                queue.TryPeek(out _, out var nextFleetEventAt);
                var batchCount = CalculateBatchCount(
                    route,
                    unlockState,
                    settings,
                    mutable.Rank,
                    mutable.CurrentExp,
                    mutable.NextLevelExp,
                    mutable.VoyageCount,
                    fleetMode: true,
                    mutable.NextAvailableAt,
                    readyToDispatch.Count > 1
                        ? mutable.NextAvailableAt
                        : queue.Count > 0 ? nextFleetEventAt : null);
                mutable.PendingVoyage = CreatePendingVoyage(
                    route,
                    build.Code,
                    settings,
                    mutable.NextAvailableAt,
                    batchCount);
                mutable.NextAvailableAt = mutable.PendingVoyage.ReturnAtUtc;
                queue.Enqueue(submarineId, mutable.NextAvailableAt);
            }
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
        DateTimeOffset? deadlineUtc,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var plans = new List<VoyagePlan>();
        var rank = sub.Rank;
        var currentExp = sub.CurrentExp;
        var nextLevelExp = sub.NextLevelExp;
        var nextAvailable = GetStartingAvailableTime(sub, settings, now);
        var voyageCount = 0;

        if (rank >= settings.TargetRank)
        {
            return CreatePerSubResult(
                sub,
                settings,
                now,
                rank,
                now,
                0,
                plans,
                unlockState,
                warnings);
        }

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
            cancellationToken.ThrowIfCancellationRequested();
            if (IsTimedOut(deadlineUtc))
            {
                warnings.Add($"Calculation time limit reached for {sub.Name}; ETA is partial.");
                break;
            }

            var build = buildResolver.ResolveBuildForRank(rank, settings);
            var tempSub = sub with { Rank = rank, CurrentExp = currentExp, NextLevelExp = nextLevelExp };
            var route = routeSelector.SelectNextRoute(
                tempSub,
                unlockState,
                build,
                settings,
                fleetMode,
                deadlineUtc,
                cancellationToken);
            if (route.Route.Count == 0 || route.Exp == 0)
            {
                warnings.Add($"No valid route found for {sub.Name}; ETA is incomplete.");
                break;
            }

            var batchCount = CalculateBatchCount(
                route,
                unlockState,
                settings,
                rank,
                currentExp,
                nextLevelExp,
                voyageCount,
                fleetMode,
                nextAvailable,
                null);
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

            plans.Add(plan);
        }

        if (voyageCount >= settings.SimulationSafetyVoyageCapPerSubmarine && rank < settings.TargetRank)
            warnings.Add($"Simulation stopped for {sub.Name} after {settings.SimulationSafetyVoyageCapPerSubmarine} voyages.");

        return CreatePerSubResult(sub, settings, now, rank, nextAvailable, voyageCount, plans, unlockState, warnings);
    }

    private PendingVoyage CreatePendingVoyage(
        RouteCandidate route,
        string buildCode,
        EtaSettings settings,
        DateTimeOffset departAt,
        int repeatCount)
    {
        var perVoyageDuration = route.Duration + TimeSpan.FromMinutes(settings.CollectionDelayMinutes);
        return new PendingVoyage(
            route.Route,
            buildCode,
            route.Exp,
            departAt,
            departAt + TimeSpan.FromTicks(perVoyageDuration.Ticks * repeatCount),
            perVoyageDuration,
            repeatCount,
            route.EtaModel,
            route.DurationCapApplied,
            IsCurrentVoyage: false);
    }

    private VoyagePlan? CompletePendingVoyage(
        MutableSubState state,
        UnlockState unlockState,
        EtaSettings settings)
    {
        var pending = state.PendingVoyage!;
        var rankBefore = state.Rank;
        var expBefore = state.CurrentExp;
        var gainedExp = checked((uint)Math.Min(
            (ulong)uint.MaxValue,
            (ulong)pending.ExpPerVoyage * (ulong)pending.RepeatCount));
        var rankResult = catalog.ApplyExp(state.Rank, state.CurrentExp, gainedExp, settings.TargetRank);
        var unlocks = unlockGraph.MarkRouteReturn(
            pending.Route,
            unlockState,
            state.Source.SubmarineId,
            pending.ReturnAtUtc);

        state.Rank = rankResult.Rank;
        state.CurrentExp = rankResult.CurrentExp;
        state.NextLevelExp = rankResult.NextLevelExp;
        state.NextAvailableAt = pending.ReturnAtUtc;

        if (pending.IsCurrentVoyage)
            return null;

        return new VoyagePlan(
            state.Source.SubmarineId,
            state.Source.Name,
            pending.DepartAtUtc,
            pending.ReturnAtUtc,
            pending.BuildCode,
            pending.Route,
            gainedExp,
            rankBefore,
            rankResult.Rank,
            expBefore,
            rankResult.CurrentExp,
            unlocks,
            pending.RepeatCount > 1 ? [$"Batched {pending.RepeatCount} identical voyages."] : [],
            pending.ReturnAtUtc - pending.DepartAtUtc,
            pending.ExpPerVoyage / Math.Max(pending.PerVoyageDuration.TotalHours, 0.01),
            pending.EtaModel,
            pending.DurationCapApplied,
            pending.RepeatCount,
            pending.ExpPerVoyage,
            pending.PerVoyageDuration);
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
            ? unlockGraph.MarkRouteReturn(route.Route, unlockState, unlockSubmarineId, returnAt)
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
            route.DurationCapApplied,
            batchCount,
            route.Exp,
            perVoyageDuration);
    }

    private int CalculateBatchCount(
        RouteCandidate route,
        UnlockState unlockState,
        EtaSettings settings,
        int rank,
        uint currentExp,
        uint nextLevelExp,
        int voyageCount,
        bool fleetMode,
        DateTimeOffset departAt,
        DateTimeOffset? nextFleetEventAt)
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
        var batchCount = Math.Clamp(voyagesToNextRank, 1, remainingCap);
        if (fleetMode && nextFleetEventAt is not null)
        {
            var perVoyageDuration = route.Duration + TimeSpan.FromMinutes(settings.CollectionDelayMinutes);
            if (perVoyageDuration > TimeSpan.Zero)
            {
                var availableTicks = Math.Max(0, (nextFleetEventAt.Value - departAt).Ticks);
                var voyagesBeforeEvent = (int)(availableTicks / perVoyageDuration.Ticks);
                if (voyagesBeforeEvent > 0)
                    batchCount = Math.Min(batchCount, voyagesBeforeEvent);
                else
                    batchCount = 1;
            }
        }

        return batchCount;
    }

    private bool CanBatch(RouteCandidate route, UnlockState unlockState, EtaSettings settings, bool fleetMode)
    {
        if (settings.EtaModel != EtaModel.PracticalLeveling || route.AdvancesUnlockObjective)
            return false;
        if (route.UnlockTargets.Count > 0)
            return false;
        if (route.Route.Any(point => !unlockState.ExploredPoints.Contains(point)))
            return false;

        return true;
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
            plans.ToArray(),
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
        var exp = catalog.CalculateExp(route, build, settings.GetEffectiveExpMode());
        var rankResult = catalog.ApplyExp(rank, currentExp, exp, settings.TargetRank);
        var returnAt = sub.ReturnAtUtc + TimeSpan.FromMinutes(settings.CollectionDelayMinutes);
        unlockGraph.MarkRouteReturn(route, unlockState, sub.SubmarineId, returnAt);

        return new CurrentVoyageApplication(rankResult.Rank, rankResult.CurrentExp, rankResult.NextLevelExp, returnAt);
    }

    private static UnlockState CreateUnlockState(FcState fc) => new(
        new HashSet<uint>(fc.UnlockedPoints),
        new HashSet<uint>(fc.ExploredPoints),
        [],
        [])
    {
        KnownSubmarineSlots = Math.Clamp(fc.Submarines.Count, 1, 4),
    };

    private static DateTimeOffset GetStartingAvailableTime(SubmarineState sub, EtaSettings settings, DateTimeOffset now)
    {
        return sub.ReturnAtUtc > now
            ? sub.ReturnAtUtc + TimeSpan.FromMinutes(settings.CollectionDelayMinutes)
            : now;
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

        public PendingVoyage? PendingVoyage { get; set; }
    }

    private sealed record PendingVoyage(
        IReadOnlyList<uint> Route,
        string BuildCode,
        uint ExpPerVoyage,
        DateTimeOffset DepartAtUtc,
        DateTimeOffset ReturnAtUtc,
        TimeSpan PerVoyageDuration,
        int RepeatCount,
        EtaModel EtaModel,
        bool DurationCapApplied,
        bool IsCurrentVoyage);

    private sealed record CurrentVoyageApplication(
        int Rank,
        uint CurrentExp,
        uint NextLevelExp,
        DateTimeOffset NextAvailableAt);
}
