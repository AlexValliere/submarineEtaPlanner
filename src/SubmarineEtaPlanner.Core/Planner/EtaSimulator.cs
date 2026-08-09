namespace SubmarineEtaPlanner.Planner;

public sealed class EtaSimulator(
    BuildResolver buildResolver,
    RouteUnlockGraph unlockGraph,
    RouteSelector routeSelector,
    ISubmarineCatalog catalog) : IEtaSimulator
{
    private const int TargetProbabilitySamples = 256;
    private const int MinimumProbabilitySamples = 64;

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
        => Simulate(
            fc,
            settings,
            EtaSimulationScope.CreateDefault(fc, settings.TargetRank),
            now,
            deadlineUtc,
            cancellationToken);

    public EtaResult Simulate(
        FcState fc,
        EtaSettings settings,
        EtaSimulationScope scope,
        DateTimeOffset now,
        DateTimeOffset? deadlineUtc,
        CancellationToken cancellationToken)
        => SimulateProbabilistic(fc, settings, now, deadlineUtc, cancellationToken);

    private EtaResult SimulateTrial(
        FcState fc,
        EtaSettings settings,
        DateTimeOffset now,
        DateTimeOffset? deadlineUtc,
        CancellationToken cancellationToken,
        Func<UnlockRule, bool> unlockSucceeded)
        => settings.SimulationMode switch
        {
            SimulationMode.OptimisticPerSub => SimulateOptimistic(fc, settings, now, deadlineUtc, cancellationToken, unlockSucceeded),
            _ => SimulateFleet(fc, settings, now, deadlineUtc, cancellationToken, unlockSucceeded),
        };

    private EtaResult SimulateProbabilistic(
        FcState fc,
        EtaSettings settings,
        DateTimeOffset now,
        DateTimeOffset? deadlineUtc,
        CancellationToken cancellationToken)
    {
        var probability = Math.Clamp(settings.UnlockSuccessProbability, 0.01, 1.0);
        var trials = new List<EtaResult>(TargetProbabilitySamples);
        for (var sample = 0; sample < TargetProbabilitySamples; sample++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sample > 0 && IsTimedOut(deadlineUtc))
                break;

            var random = new Random(CreateDeterministicSeed(fc, settings, sample));
            var trial = SimulateTrial(
                fc,
                settings,
                now,
                deadlineUtc,
                cancellationToken,
                _ => random.NextDouble() < probability);
            trials.Add(trial);
            if (!trial.IsComplete && IsTimedOut(deadlineUtc))
                break;
            if ((sample + 1) >= MinimumProbabilitySamples &&
                (sample + 1) % 32 == 0 &&
                ProbabilityForecastConverged(trials, now))
            {
                break;
            }
        }

        return AggregateProbabilityTrials(fc, settings, now, trials);
    }

    private EtaResult SimulateOptimistic(
        FcState fc,
        EtaSettings settings,
        DateTimeOffset now,
        DateTimeOffset? deadlineUtc,
        CancellationToken cancellationToken,
        Func<UnlockRule, bool> unlockSucceeded)
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
            var result = SimulateSingleSub(sub, unlockState, settings, now, fleetMode: false, deadlineUtc, cancellationToken, unlockSucceeded);
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
        CancellationToken cancellationToken,
        Func<UnlockRule, bool> unlockSucceeded)
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

            var currentRoute = GetKnownCurrentRoute(state.Source);
            if (currentRoute.Count > 0 && state.Source.CurrentVoyageKnown)
            {
                var currentBuild = catalog.ResolveBuild(state.Source.BuildParts, state.Rank) ??
                                   buildResolver.ResolveBuildForRank(state.Rank, settings);
                var currentReturn = GetProjectedCollectionAt(state.Source, settings, now);
                var reservedUnlocks = unlockGraph.ReserveRoute(currentRoute, unlockState);
                state.PendingVoyage = new PendingVoyage(
                    currentRoute,
                    currentBuild.Code,
                    catalog.CalculateExp(currentRoute, currentBuild, settings.GetEffectiveExpMode()),
                    now,
                    currentReturn,
                    currentReturn - now,
                    1,
                    settings.EtaModel,
                    DurationCapApplied: false,
                    IsCurrentVoyage: true,
                    ReservedUnlockTargets: reservedUnlocks,
                    UnlockObjective: null);
                state.NextAvailableAt = currentReturn;
                state.CurrentVoyageApplied = true;
            }

            if (state.Source.ReturnAtUtc <= now && currentRoute.Count == 0)
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
                    var completedPlan = CompletePendingVoyage(mutable, unlockState, settings, unlockSucceeded);
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
                status,
                reason)
            {
                CurrentRoute = GetCurrentRoute(state.Source),
                CurrentReturnAtUtc = GetCurrentReturnAtUtc(state.Source),
                CurrentVoyageUnknown = IsCurrentVoyageUnknown(state.Source, now),
            };
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
        CancellationToken cancellationToken,
        Func<UnlockRule, bool> unlockSucceeded)
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
            nextAvailable,
            unlockSucceeded);
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
                batchCount,
                unlockSucceeded);

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
            IsCurrentVoyage: false,
            ReservedUnlockTargets: route.UnlockTargets,
            UnlockObjective: route.UnlockObjective);
    }

    private VoyagePlan? CompletePendingVoyage(
        MutableSubState state,
        UnlockState unlockState,
        EtaSettings settings,
        Func<UnlockRule, bool> unlockSucceeded)
    {
        var pending = state.PendingVoyage!;
        var rankBefore = state.Rank;
        var expBefore = state.CurrentExp;
        var gainedExp = checked((uint)Math.Min(
            (ulong)uint.MaxValue,
            (ulong)pending.ExpPerVoyage * (ulong)pending.RepeatCount));
        var rankResult = catalog.ApplyExp(state.Rank, state.CurrentExp, gainedExp, settings.TargetRank);
        unlockGraph.ReleaseRouteReservations(pending.ReservedUnlockTargets, unlockState);
        var unlocks = unlockGraph.MarkRouteReturn(
            pending.Route,
            unlockState,
            state.Source.SubmarineId,
            pending.ReturnAtUtc,
            unlockSucceeded);

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
            pending.PerVoyageDuration)
        {
            UnlockObjective = pending.UnlockObjective,
        };
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
        int batchCount,
        Func<UnlockRule, bool> unlockSucceeded)
    {
        var perVoyageDuration = route.Duration + TimeSpan.FromMinutes(settings.CollectionDelayMinutes);
        var returnAt = departAt + TimeSpan.FromTicks(perVoyageDuration.Ticks * batchCount);
        var gainedExp = checked((uint)Math.Min((ulong)uint.MaxValue, (ulong)route.Exp * (ulong)batchCount));
        var rankResult = catalog.ApplyExp(rank, currentExp, gainedExp, settings.TargetRank);
        IReadOnlyList<uint> unlocks;
        if (batchCount == 1)
        {
            unlockGraph.ReleaseRouteReservations(route.UnlockTargets, unlockState);
            unlocks = unlockGraph.MarkRouteReturn(route.Route, unlockState, unlockSubmarineId, returnAt, unlockSucceeded);
        }
        else
        {
            unlocks = Array.Empty<uint>();
        }
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
            perVoyageDuration)
        {
            UnlockObjective = route.UnlockObjective,
        };
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
            status,
            reason)
        {
            CurrentRoute = GetCurrentRoute(sub),
            CurrentReturnAtUtc = GetCurrentReturnAtUtc(sub),
            CurrentVoyageUnknown = IsCurrentVoyageUnknown(sub, now),
        };
    }

    private static IReadOnlyList<uint> GetKnownCurrentRoute(SubmarineState sub)
        => sub.ManualCurrentRouteOverride.Count > 0
            ? sub.ManualCurrentRouteOverride
            : sub.CurrentRoute;

    private static IReadOnlyList<uint> GetCurrentRoute(SubmarineState sub)
    {
        if (!sub.CurrentVoyageKnown)
            return [];

        return GetKnownCurrentRoute(sub).ToArray();
    }

    private static DateTimeOffset? GetCurrentReturnAtUtc(SubmarineState sub)
        => sub.CurrentVoyageKnown && GetKnownCurrentRoute(sub).Count > 0
            ? sub.ReturnAtUtc
            : null;

    private static bool IsCurrentVoyageUnknown(SubmarineState sub, DateTimeOffset now)
        => sub.ReturnAtUtc > now && !sub.CurrentVoyageKnown;

    private CurrentVoyageApplication ApplyCurrentVoyageIfKnown(
        SubmarineState sub,
        UnlockState unlockState,
        EtaSettings settings,
        DateTimeOffset now,
        int rank,
        uint currentExp,
        uint nextLevelExp,
        DateTimeOffset nextAvailable,
        Func<UnlockRule, bool> unlockSucceeded)
    {
        var route = GetKnownCurrentRoute(sub);
        if (!sub.CurrentVoyageKnown || route.Count == 0)
            return new CurrentVoyageApplication(rank, currentExp, nextLevelExp, nextAvailable);

        var build = catalog.ResolveBuild(sub.BuildParts, rank) ?? buildResolver.ResolveBuildForRank(rank, settings);
        var exp = catalog.CalculateExp(route, build, settings.GetEffectiveExpMode());
        var rankResult = catalog.ApplyExp(rank, currentExp, exp, settings.TargetRank);
        var returnAt = GetProjectedCollectionAt(sub, settings, now);
        unlockGraph.MarkRouteReturn(route, unlockState, sub.SubmarineId, returnAt, unlockSucceeded);

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
        return sub.ReturnAtUtc > now || sub.CurrentVoyageKnown && GetKnownCurrentRoute(sub).Count > 0
            ? GetProjectedCollectionAt(sub, settings, now)
            : now;
    }

    private static DateTimeOffset GetProjectedCollectionAt(SubmarineState sub, EtaSettings settings, DateTimeOffset now)
    {
        var expectedCollectionAt = sub.ReturnAtUtc + TimeSpan.FromMinutes(settings.CollectionDelayMinutes);
        return expectedCollectionAt > now ? expectedCollectionAt : now;
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

    private EtaResult AggregateProbabilityTrials(
        FcState fc,
        EtaSettings settings,
        DateTimeOffset now,
        IReadOnlyList<EtaResult> trials)
    {
        if (trials.Count == 0)
            throw new InvalidOperationException($"No probability samples were produced for {fc.DisplayName}.");

        var completed = trials.Where(result => result.IsComplete).OrderBy(result => result.FcCompletionAtUtc).ToArray();
        var usable = completed.Length > 0 ? completed : trials.OrderBy(result => result.FcCompletionAtUtc).ToArray();
        var fcForecast = CreatePercentiles(usable.Select(result => result.FcCompletionAtUtc), usable.Length);
        var representative = usable
            .OrderBy(result => Math.Abs((result.FcCompletionAtUtc - fcForecast.P50AtUtc).Ticks))
            .ThenBy(result => result.VoyageCount)
            .First();

        var perSubResults = representative.PerSubResults.Select(selected =>
        {
            var sampled = usable
                .SelectMany(result => result.PerSubResults)
                .Where(result => result.SubmarineId == selected.SubmarineId)
                .ToArray();
            var forecast = CreatePercentiles(sampled.Select(result => result.EtaAtUtc), sampled.Length);
            var outcomes = sampled
                .Where(result => result.NextRoute.Count > 0)
                .GroupBy(result => string.Join(",", result.NextRoute))
                .Select(group =>
                {
                    var route = group.First().NextRoute.ToArray();
                    return new RouteOutcome(
                        route,
                        group.Count() / (double)Math.Max(1, sampled.Length),
                        route.Where(point => !fc.UnlockedPoints.Contains(point)).Distinct().ToArray());
                })
                .OrderByDescending(outcome => outcome.Probability)
                .ThenBy(outcome => string.Join(",", outcome.Route))
                .ToArray();
            var preview = selected.VoyagePreview.Select(plan => MarkProjectedPlan(plan, fc.UnlockedPoints)).ToArray();

            return selected with
            {
                EtaAtUtc = forecast.P50AtUtc,
                Remaining = forecast.P50AtUtc - now,
                VoyagePreview = preview,
                EtaForecast = forecast,
                NextRouteOutcomes = outcomes,
            };
        }).ToArray();

        var sampleWarning = completed.Length < MinimumProbabilitySamples
            ? $"Only {completed.Length} of {MinimumProbabilitySamples} minimum probability samples completed; ETA ranges are partial."
            : null;
        var warnings = representative.Warnings
            .Concat(sampleWarning is null ? [] : [sampleWarning])
            .Distinct()
            .ToArray();
        var status = sampleWarning is null && representative.IsComplete
            ? CalculationStatus.Complete
            : CalculationStatus.Partial;
        var reason = status == CalculationStatus.Complete
            ? null
            : sampleWarning ?? representative.IncompleteReason;

        return representative with
        {
            PerSubResults = perSubResults,
            FcCompletionAtUtc = fcForecast.P50AtUtc,
            PlannedRoutes = representative.PlannedRoutes.Select(plan => MarkProjectedPlan(plan, fc.UnlockedPoints)).ToArray(),
            Warnings = warnings,
            Status = status,
            IncompleteReason = reason,
            CompletionForecast = fcForecast,
            ActiveUnlockAttempts = GetActiveUnlockAttempts(fc, settings, now),
            ProbabilitySampleCount = completed.Length,
        };
    }

    private static VoyagePlan MarkProjectedPlan(VoyagePlan plan, IReadOnlySet<uint> initiallyUnlocked)
    {
        var required = plan.Route.Where(point => !initiallyUnlocked.Contains(point)).Distinct().ToArray();
        return plan with
        {
            DependsOnProjectedUnlocks = required.Length > 0,
            RequiredProjectedUnlocks = required,
        };
    }

    private IReadOnlyList<UnlockAttemptForecast> GetActiveUnlockAttempts(
        FcState fc,
        EtaSettings settings,
        DateTimeOffset now)
    {
        var state = CreateUnlockState(fc);
        var attempts = new List<(UnlockRule Rule, SubmarineState Submarine)>();
        foreach (var submarine in fc.Submarines.Where(submarine => submarine.ReturnAtUtc > now && submarine.CurrentVoyageKnown))
        {
            var route = submarine.ManualCurrentRouteOverride.Count > 0
                ? submarine.ManualCurrentRouteOverride
                : submarine.CurrentRoute;
            foreach (var source in route.Distinct())
            {
                var rule = unlockGraph.GetNextLockedRuleForSource(source, state, submarine.Rank);
                if (rule is not null)
                    attempts.Add((rule, submarine));
            }
        }

        var probability = Math.Clamp(settings.UnlockSuccessProbability, 0.01, 1.0);
        return attempts
            .GroupBy(item => (item.Rule.SourcePoint, item.Rule.UnlocksPoint))
            .Select(group =>
            {
                var submarines = group.Select(item => item.Submarine).DistinctBy(submarine => submarine.SubmarineId).ToArray();
                return new UnlockAttemptForecast(
                    group.Key.SourcePoint,
                    group.Key.UnlocksPoint,
                    submarines.Select(submarine => submarine.SubmarineId).ToArray(),
                    submarines.Select(submarine => submarine.Name).ToArray(),
                    submarines.Min(submarine => submarine.ReturnAtUtc),
                    submarines.Max(submarine => submarine.ReturnAtUtc),
                    1.0 - Math.Pow(1.0 - probability, submarines.Length));
            })
            .OrderBy(attempt => attempt.LatestReturnAtUtc)
            .ThenBy(attempt => attempt.TargetPoint)
            .ToArray();
    }

    private static EtaPercentiles CreatePercentiles(IEnumerable<DateTimeOffset> values, int sampleCount)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        if (ordered.Length == 0)
            throw new InvalidOperationException("Cannot calculate percentiles without samples.");

        DateTimeOffset At(double percentile)
        {
            var index = (int)Math.Round((ordered.Length - 1) * percentile, MidpointRounding.AwayFromZero);
            return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
        }

        return new EtaPercentiles(At(0.10), At(0.50), At(0.90), sampleCount);
    }

    private static bool ProbabilityForecastConverged(IReadOnlyList<EtaResult> trials, DateTimeOffset now)
    {
        var completed = trials.Where(result => result.IsComplete).Select(result => result.FcCompletionAtUtc).ToArray();
        if (completed.Length < MinimumProbabilitySamples || completed.Length < 64)
            return false;

        var previousCount = completed.Length - 32;
        if (previousCount < 32)
            return false;

        var current = CreatePercentiles(completed, completed.Length);
        var previous = CreatePercentiles(completed.Take(previousCount), previousCount);
        var medianDuration = current.P50AtUtc - now;
        var tolerance = TimeSpan.FromTicks(Math.Max(
            TimeSpan.FromHours(6).Ticks,
            (long)(Math.Abs(medianDuration.Ticks) * 0.01)));

        return (current.P10AtUtc - previous.P10AtUtc).Duration() <= tolerance &&
               (current.P50AtUtc - previous.P50AtUtc).Duration() <= tolerance &&
               (current.P90AtUtc - previous.P90AtUtc).Duration() <= tolerance;
    }

    private static int CreateDeterministicSeed(FcState fc, EtaSettings settings, int sample)
    {
        unchecked
        {
            uint hash = 2166136261;
            void Add(long value)
            {
                for (var shift = 0; shift < 64; shift += 8)
                {
                    hash ^= (byte)(value >> shift);
                    hash *= 16777619;
                }
            }

            foreach (var value in fc.FcId)
                Add(value);
            foreach (var point in fc.UnlockedPoints.Order())
                Add(point);
            foreach (var submarine in fc.Submarines.OrderBy(submarine => submarine.SubmarineId))
            {
                Add(submarine.SubmarineId);
                Add(submarine.Rank);
                Add(submarine.CurrentExp);
                Add(submarine.ReturnAtUtc.UtcTicks);
                foreach (var point in submarine.CurrentRoute)
                    Add(point);
            }

            Add(settings.TargetRank);
            Add(BitConverter.DoubleToInt64Bits(Math.Clamp(settings.UnlockSuccessProbability, 0.01, 1.0)));
            Add((int)settings.EtaModel);
            Add((int)settings.RouteGoal);
            Add(sample);
            return (int)hash;
        }
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
        bool IsCurrentVoyage,
        IReadOnlyList<uint> ReservedUnlockTargets,
        UnlockObjective? UnlockObjective);

    private sealed record CurrentVoyageApplication(
        int Rank,
        uint CurrentExp,
        uint NextLevelExp,
        DateTimeOffset NextAvailableAt);
}
