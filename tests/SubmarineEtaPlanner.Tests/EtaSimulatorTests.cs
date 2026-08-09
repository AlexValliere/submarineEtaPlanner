using SubmarineEtaPlanner.Planner;
using SubmarineEtaPlanner.SubmarineTrackerCompat;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class EtaSimulatorTests
{
    [Fact]
    public void RankUpCarriesExpForward()
    {
        var catalog = new CompatSubmarineCatalog();

        var result = catalog.ApplyExp(10, 0, 100_000, 50);

        Assert.True(result.Rank > 10);
        Assert.True(result.CurrentExp < result.NextLevelExp || result.NextLevelExp == 0);
    }

    [Fact]
    public void StopsAtTargetRank()
    {
        var simulator = CreateSimulator();
        var settings = EtaSettings.CreateDefault();
        settings.TargetRank = 90;
        var fc = CreateFc(CreateSub(rank: 89, currentExp: 500_000));

        var result = simulator.Simulate(fc, settings, DateTimeOffset.UnixEpoch);

        Assert.All(result.PerSubResults, sub => Assert.True(sub.FinalRank >= settings.TargetRank));
        Assert.DoesNotContain(result.PlannedRoutes, p => p.RankBefore >= settings.TargetRank);
    }

    [Fact]
    public void AutoOnlyScopeKeepsExistingSimulatorOutput()
    {
        var simulator = CreateSimulator(new ScriptedCatalog(routeExp: 100));
        var settings = EtaSettings.CreateDefault() with
        {
            TargetRank = 2,
            UnlockSuccessProbability = 1.0,
        };
        var fc = CreateFc(new HashSet<uint> { 99 }, CreateSub(rank: 1) with { NextLevelExp = 100 });
        var now = DateTimeOffset.UnixEpoch;

        var legacy = simulator.Simulate(fc, settings, now);
        var scoped = simulator.Simulate(
            fc,
            settings,
            EtaSimulationScope.CreateDefault(fc, settings.TargetRank),
            now,
            deadlineUtc: null,
            CancellationToken.None);

        Assert.Equal(legacy.Status, scoped.Status);
        Assert.Equal(legacy.FcCompletionAtUtc, scoped.FcCompletionAtUtc);
        Assert.Equal(legacy.ProbabilitySampleCount, scoped.ProbabilitySampleCount);
        Assert.Equal(
            legacy.PerSubResults.Select(result => (result.SubmarineId, result.FinalRank, result.VoyageCount, result.EtaAtUtc)),
            scoped.PerSubResults.Select(result => (result.SubmarineId, result.FinalRank, result.VoyageCount, result.EtaAtUtc)));
        Assert.Equal(legacy.PlannedRoutes.Count, scoped.PlannedRoutes.Count);
    }

    [Theory]
    [InlineData(SimulationMode.Fleet)]
    [InlineData(SimulationMode.OptimisticPerSub)]
    public void MixedFleetOnlyPlansFutureVoyagesForLevelingTarget(SimulationMode simulationMode)
    {
        var simulator = CreateSimulator(new ScriptedCatalog(routeExp: 100));
        var settings = EtaSettings.CreateDefault() with
        {
            TargetRank = 2,
            SimulationMode = simulationMode,
            UnlockSuccessProbability = 1.0,
            CollectionDelayMinutes = 0,
        };
        var fc = CreateFc(
            CreateSub(1, "Farming A", 1) with { NextLevelExp = 100 },
            CreateSub(2, "Farming B", 1) with { NextLevelExp = 100 },
            CreateSub(3, "Farming C", 1) with { NextLevelExp = 100 },
            CreateSub(4, "Leveling", 1) with { NextLevelExp = 100 });
        var now = DateTimeOffset.UnixEpoch;

        var result = SimulateScoped(simulator, fc, settings, now, 4);

        var leveling = Assert.Single(result.PerSubResults, sub => sub.IncludedInLevelingTarget);
        Assert.Equal(4, leveling.SubmarineId);
        Assert.Equal(leveling.EtaAtUtc, result.FcCompletionAtUtc);
        Assert.Equal(leveling.VoyageCount, result.VoyageCount);
        Assert.True(leveling.VoyageCount > 0);
        Assert.All(
            result.PerSubResults.Where(sub => !sub.IncludedInLevelingTarget),
            passive =>
            {
                Assert.Equal(0, passive.VoyageCount);
                Assert.Empty(passive.VoyagePreview);
                Assert.Empty(passive.NextRoute);
                Assert.Equal(passive.StartingRank, passive.FinalRank);
                Assert.Equal(now, passive.EtaAtUtc);
            });
        Assert.All(result.PlannedRoutes, plan => Assert.Equal(4, plan.SubmarineId));
    }

    [Fact]
    public void FarmingSubmarineBelowTargetReceivesNoLevelingRoute()
    {
        var simulator = CreateSimulator(new ScriptedCatalog(routeExp: 100));
        var settings = EtaSettings.CreateDefault() with
        {
            TargetRank = 2,
            UnlockSuccessProbability = 1.0,
            CollectionDelayMinutes = 0,
        };
        var farming = CreateSub(1, "Farming", 1) with { NextLevelExp = 100 };
        var leveling = CreateSub(2, "Leveling", 1) with { NextLevelExp = 100 };

        var result = SimulateScoped(
            simulator,
            CreateFc(farming, leveling),
            settings,
            DateTimeOffset.UnixEpoch,
            leveling.SubmarineId);

        var farmingResult = Assert.Single(result.PerSubResults, sub => sub.SubmarineId == farming.SubmarineId);
        Assert.False(farmingResult.IncludedInLevelingTarget);
        Assert.Equal(0, farmingResult.VoyageCount);
        Assert.DoesNotContain(result.PlannedRoutes, plan => plan.SubmarineId == farming.SubmarineId);
    }

    [Theory]
    [InlineData(SimulationMode.Fleet)]
    [InlineData(SimulationMode.OptimisticPerSub)]
    public void PausedUnderwayVoyageCanUnlockButDoesNotScheduleSecondVoyage(SimulationMode simulationMode)
    {
        var catalog = new ScriptedCatalog([new UnlockRule(7, 8, 1, 1, IsMainProgression: true)]);
        var simulator = CreateSimulator(catalog);
        var settings = EtaSettings.CreateDefault() with
        {
            TargetRank = 2,
            SimulationMode = simulationMode,
            UnlockSuccessProbability = 1.0,
            CollectionDelayMinutes = 0,
        };
        var returnAt = DateTimeOffset.UnixEpoch.AddHours(1);
        var paused = CreateSub(1, "Paused", 1) with
        {
            ReturnAtUtc = returnAt,
            CurrentRoute = [7],
            CurrentVoyageKnown = true,
        };
        var leveling = CreateSub(2, "Leveling", 1) with
        {
            NextLevelExp = 100,
            ReturnAtUtc = returnAt,
            CurrentVoyageKnown = false,
        };

        var result = SimulateScoped(
            simulator,
            CreateFc(new HashSet<uint>([7]), paused, leveling),
            settings,
            DateTimeOffset.UnixEpoch,
            leveling.SubmarineId);

        var pausedResult = Assert.Single(result.PerSubResults, sub => sub.SubmarineId == paused.SubmarineId);
        Assert.False(pausedResult.IncludedInLevelingTarget);
        Assert.Equal(paused.Rank, pausedResult.FinalRank);
        Assert.Equal(0, pausedResult.VoyageCount);
        Assert.Empty(pausedResult.VoyagePreview);
        Assert.Contains(result.UnlockMilestones, milestone =>
            milestone.SubmarineId == paused.SubmarineId && milestone.UnlockedPoint == 8);
        Assert.Contains(catalog.ObservedUnlockedStates, points => points.Contains(8));
        Assert.DoesNotContain(result.PlannedRoutes, plan => plan.SubmarineId == paused.SubmarineId);
    }

    [Fact]
    public void NoLevelingTargetsCompletesImmediately()
    {
        var catalog = new ScriptedCatalog(routeExp: 100);
        var simulator = CreateSimulator(catalog);
        var settings = EtaSettings.CreateDefault() with { TargetRank = 2 };
        var now = DateTimeOffset.UnixEpoch.AddDays(10);
        var fc = CreateFc(CreateSub(rank: 1));

        var result = SimulateScoped(simulator, fc, settings, now);

        Assert.True(result.IsComplete);
        Assert.Equal(now, result.FcCompletionAtUtc);
        Assert.Equal(0, result.VoyageCount);
        Assert.Empty(result.PlannedRoutes);
        Assert.Empty(catalog.ObservedUnlockedStates);
        Assert.All(result.PerSubResults, sub => Assert.False(sub.IncludedInLevelingTarget));
    }

    [Fact]
    public void PassiveVoyageReturningAfterTargetsFinishDoesNotExtendCompletion()
    {
        var simulator = CreateSimulator(new ScriptedCatalog(
            [new UnlockRule(7, 8, 1, 1, IsMainProgression: true)],
            routeExp: 100,
            routeDuration: TimeSpan.FromHours(1)));
        var settings = EtaSettings.CreateDefault() with
        {
            TargetRank = 2,
            UnlockSuccessProbability = 1.0,
            CollectionDelayMinutes = 0,
        };
        var passive = CreateSub(1, "Farming", 1) with
        {
            ReturnAtUtc = DateTimeOffset.UnixEpoch.AddHours(10),
            CurrentRoute = [7],
            CurrentVoyageKnown = true,
        };
        var target = CreateSub(2, "Leveling", 1) with { NextLevelExp = 100 };

        var result = SimulateScoped(
            simulator,
            CreateFc(new HashSet<uint>([7, 99]), passive, target),
            settings,
            DateTimeOffset.UnixEpoch,
            target.SubmarineId);

        var targetResult = Assert.Single(result.PerSubResults, sub => sub.IncludedInLevelingTarget);
        Assert.Equal(targetResult.EtaAtUtc, result.FcCompletionAtUtc);
        Assert.True(result.FcCompletionAtUtc < passive.ReturnAtUtc);
        Assert.DoesNotContain(result.UnlockMilestones, milestone => milestone.SubmarineId == passive.SubmarineId);
    }

    [Fact]
    public void FleetModeSharesUnlockState()
    {
        var simulator = CreateSimulator();
        var settings = EtaSettings.CreateDefault() with
        {
            SimulationMode = SimulationMode.Fleet,
            EtaModel = EtaModel.ExactRouteSearch,
            UnlockSuccessProbability = 1.0,
        };
        settings.TargetRank = 22;
        var fc = CreateFc(Enumerable.Range(1, 15).Select(i => (uint)i).ToHashSet(), CreateSub(1, "A", 20), CreateSub(2, "B", 20));

        var result = simulator.Simulate(fc, settings, DateTimeOffset.UnixEpoch);

        Assert.NotEmpty(result.UnlockMilestones);
        Assert.True(result.PerSubResults.Sum(r => r.VoyageCount) > 0);
    }

    [Fact]
    public void UnlockMilestonesAreStampedAtVoyageReturn()
    {
        var simulator = CreateSimulator();
        var settings = EtaSettings.CreateDefault() with
        {
            SimulationMode = SimulationMode.Fleet,
            EtaModel = EtaModel.ExactRouteSearch,
            UnlockSuccessProbability = 1.0,
        };
        settings.TargetRank = 22;
        var fc = CreateFc(Enumerable.Range(1, 15).Select(i => (uint)i).ToHashSet(), CreateSub(1, "A", 20));

        var result = simulator.Simulate(fc, settings, DateTimeOffset.UnixEpoch);
        var milestone = Assert.Single(
            result.UnlockMilestones,
            item => item.Kind == UnlockMilestoneKind.SectorUnlocked);
        var unlockPlan = Assert.Single(result.PlannedRoutes, plan => plan.UnlocksApplied.Contains(milestone.UnlockedPoint));

        Assert.Equal(unlockPlan.ReturnAtUtc, milestone.ReturnAtUtc);
    }

    [Fact]
    public void RouteSelectorAvoidsDuplicatePendingUnlocks()
    {
        var catalog = new CompatSubmarineCatalog();
        var unlockGraph = new RouteUnlockGraph(catalog);
        var selector = new RouteSelector(catalog, unlockGraph);
        var settings = EtaSettings.CreateDefault() with
        {
            SimulationMode = SimulationMode.Fleet,
            UnlockSuccessProbability = 1.0,
            CollectionDelayMinutes = 0,
        };
        var unlocked = Enumerable.Range(1, 15).Select(i => (uint)i).ToHashSet();
        var state = new UnlockState(new HashSet<uint>(unlocked), new HashSet<uint>(unlocked), [16], []);
        var sub = CreateSub(rank: 20);
        var build = new BuildResolver(catalog).ResolveBuildForRank(sub.Rank, settings);

        var route = selector.SelectNextRoute(sub, state, build, settings, fleetMode: true);

        Assert.DoesNotContain(16u, route.UnlockTargets);
    }

    [Fact]
    public void RouteSelectorFallsBackToBestExpRoute()
    {
        var catalog = new CompatSubmarineCatalog();
        var unlockGraph = new RouteUnlockGraph(catalog);
        var selector = new RouteSelector(catalog, unlockGraph);
        var settings = EtaSettings.CreateDefault();
        var unlocked = Enumerable.Range(1, 5).Select(i => (uint)i).ToHashSet();
        var state = new UnlockState(new HashSet<uint>(unlocked), new HashSet<uint>(unlocked), [], []);
        var sub = CreateSub(rank: 1);
        var build = new BuildResolver(catalog).ResolveBuildForRank(sub.Rank, settings);

        var route = selector.SelectNextRoute(sub, state, build, settings, fleetMode: false);

        Assert.NotEmpty(route.Route);
        Assert.True(route.Exp > 0);
    }

    [Fact]
    public void FastestLevelingRouteGoalDoesNotRequestUnlockRoute()
    {
        var catalog = new ScriptedCatalog();
        var selector = new RouteSelector(catalog, new RouteUnlockGraph(catalog));
        var settings = EtaSettings.CreateDefault() with { EtaModel = EtaModel.ExactRouteSearch, RouteGoal = RouteGoal.FastestLevelingOnly };
        var state = new UnlockState([1], [1], [], []);

        var route = selector.SelectNextRoute(CreateSub(rank: 10), state, catalog.ResolveBuild("TEST", 10), settings, fleetMode: false);

        Assert.Equal([99u], route.Route);
        Assert.Empty(catalog.LastMustInclude);
        Assert.Null(route.UnlockObjective);
    }

    [Fact]
    public void PracticalLevelingRequestsReachableUnlockProgressionRoute()
    {
        var catalog = new ScriptedCatalog();
        var selector = new RouteSelector(catalog, new RouteUnlockGraph(catalog));
        var settings = EtaSettings.CreateDefault() with { EtaModel = EtaModel.PracticalLeveling };
        var state = new UnlockState([1], [1], [], []);

        var route = selector.SelectNextRoute(CreateSub(rank: 10), state, catalog.ResolveBuild("TEST", 10), settings, fleetMode: false);

        Assert.Equal([1u], route.Route);
        Assert.Equal([1u], catalog.LastMustInclude);
        Assert.Equal(UnlockObjectiveKind.MainProgression, route.UnlockObjective?.Kind);
        Assert.Equal(2u, route.UnlockObjective?.TargetPoint);
    }

    [Fact]
    public void UnlockEverythingRouteGoalRequestsReachableUnlockRoute()
    {
        var catalog = new ScriptedCatalog();
        var selector = new RouteSelector(catalog, new RouteUnlockGraph(catalog));
        var settings = EtaSettings.CreateDefault() with { EtaModel = EtaModel.ExactRouteSearch, RouteGoal = RouteGoal.UnlockEverythingThenLevel };
        var state = new UnlockState([1], [1], [], []);

        var route = selector.SelectNextRoute(CreateSub(rank: 10), state, catalog.ResolveBuild("TEST", 10), settings, fleetMode: false);

        Assert.Equal([1u], route.Route);
        Assert.Equal([1u], catalog.LastMustInclude);
        Assert.Equal(UnlockObjectiveKind.MainProgression, route.UnlockObjective?.Kind);
    }

    [Fact]
    public void UnlockSubSlotsRouteGoalRequestsPathTowardSubSlotUnlock()
    {
        var catalog = new ScriptedCatalog(
        [
            new UnlockRule(1, 2, 1),
            new UnlockRule(2, 3, 1, UnlocksSubSlot: true),
        ]);
        var selector = new RouteSelector(catalog, new RouteUnlockGraph(catalog));
        var settings = EtaSettings.CreateDefault() with { EtaModel = EtaModel.ExactRouteSearch, RouteGoal = RouteGoal.UnlockSubSlotsThenLevel };
        var state = new UnlockState([1], [1], [], []);

        var route = selector.SelectNextRoute(CreateSub(rank: 10), state, catalog.ResolveBuild("TEST", 10), settings, fleetMode: false);

        Assert.Equal([1u], route.Route);
        Assert.Equal([1u], catalog.LastMustInclude);
        Assert.Equal(UnlockObjectiveKind.ExploreSubmarineSlot, route.UnlockObjective?.Kind);
    }

    [Fact]
    public void FleetModeUsesMutableRankForRouteSelectionAfterRankUp()
    {
        var catalog = new ScriptedCatalog([new UnlockRule(2, 3, 2)]);
        var simulator = CreateSimulator(catalog);
        var settings = EtaSettings.CreateDefault() with
        {
            SimulationMode = SimulationMode.Fleet,
            EtaModel = EtaModel.ExactRouteSearch,
            RouteGoal = RouteGoal.UnlockEverythingThenLevel,
            SimulationSafetyVoyageCapPerSubmarine = 4,
        };
        settings.TargetRank = 3;
        var fc = CreateFc(new HashSet<uint>([1, 2]), CreateSub(rank: 1));

        var result = simulator.Simulate(fc, settings, DateTimeOffset.UnixEpoch);

        Assert.Equal([99u], result.PlannedRoutes[0].Route);
        Assert.Equal([2u], result.PlannedRoutes[1].Route);
        Assert.Null(result.PlannedRoutes[0].UnlockObjective);
        Assert.Equal(UnlockObjectiveKind.SectorUnlock, result.PlannedRoutes[1].UnlockObjective?.Kind);
    }

    [Fact]
    public void ResultDisplaysFirstPlanRouteAndBuildButEtaUsesLastPlan()
    {
        var catalog = new ScriptedCatalog([new UnlockRule(2, 3, 2)]);
        var simulator = CreateSimulator(catalog);
        var settings = EtaSettings.CreateDefault() with
        {
            SimulationMode = SimulationMode.Fleet,
            EtaModel = EtaModel.ExactRouteSearch,
            RouteGoal = RouteGoal.UnlockEverythingThenLevel,
            SimulationSafetyVoyageCapPerSubmarine = 4,
        };
        settings.TargetRank = 3;
        var fc = CreateFc(new HashSet<uint>([1, 2]), CreateSub(rank: 1));

        var result = simulator.Simulate(fc, settings, DateTimeOffset.UnixEpoch);
        var sub = Assert.Single(result.PerSubResults);

        Assert.Equal("R1", sub.PlannedBuild);
        Assert.Equal(result.PlannedRoutes.First().Route, sub.NextRoute);
        Assert.Equal(result.PlannedRoutes.Last().ReturnAtUtc, sub.EtaAtUtc);
    }

    [Fact]
    public void KnownCurrentVoyageAppliesExpAndUnlocksBeforeFuturePlans()
    {
        var catalog = new ScriptedCatalog([new UnlockRule(7, 8, 2)]);
        var simulator = CreateSimulator(catalog);
        var settings = EtaSettings.CreateDefault() with
        {
            SimulationMode = SimulationMode.Fleet,
            UnlockSuccessProbability = 1.0,
            CollectionDelayMinutes = 0,
        };
        settings.TargetRank = 2;
        var returnAt = DateTimeOffset.UnixEpoch.AddHours(6);
        var sub = CreateSub(rank: 1) with
        {
            BuildParts = new SubmarineBuildParts(3, 4, 1, 2),
            ReturnAtUtc = returnAt,
            CurrentRoute = [7],
            CurrentVoyageKnown = true,
        };
        var fc = CreateFc(new HashSet<uint>([7]), sub);

        var result = simulator.Simulate(fc, settings, DateTimeOffset.UnixEpoch);
        var subResult = Assert.Single(result.PerSubResults);

        Assert.Equal(2, subResult.FinalRank);
        Assert.Equal(0, subResult.VoyageCount);
        Assert.Equal(returnAt, subResult.EtaAtUtc);
        Assert.Equal([7u], subResult.CurrentRoute);
        Assert.Equal(returnAt, subResult.CurrentReturnAtUtc);
        Assert.Empty(subResult.NextRoute);
        Assert.Contains(result.UnlockMilestones, milestone => milestone.UnlockedPoint == 8);
        Assert.True(catalog.PartBuildResolutionCount >= 1);
    }

    [Fact]
    public void ReturnedUncollectedVoyageIsProjectedOnceOnFreshSimulation()
    {
        var catalog = new ScriptedCatalog(routeExp: 100);
        var simulator = CreateSimulator(catalog);
        var settings = EtaSettings.CreateDefault() with
        {
            SimulationMode = SimulationMode.Fleet,
            CollectionDelayMinutes = 0,
        };
        settings.TargetRank = 2;
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        var sub = CreateSub(rank: 1, currentExp: 950) with
        {
            NextLevelExp = 1000,
            ReturnAtUtc = now.AddHours(-1),
            CurrentRoute = [7],
            CurrentVoyageKnown = true,
        };

        var result = simulator.Simulate(CreateFc(new HashSet<uint>([7]), sub), settings, now);
        var subResult = Assert.Single(result.PerSubResults);

        Assert.Equal(2, subResult.FinalRank);
        Assert.Equal(0, subResult.VoyageCount);
        Assert.Empty(subResult.VoyagePreview);
        Assert.Equal([7u], subResult.CurrentRoute);
        Assert.Equal(sub.ReturnAtUtc, subResult.CurrentReturnAtUtc);
    }

    [Fact]
    public void OptimisticModeProducesPerSubPlans()
    {
        var simulator = CreateSimulator();
        var settings = EtaSettings.CreateDefault() with { SimulationMode = SimulationMode.OptimisticPerSub };
        settings.TargetRank = 18;
        var fc = CreateFc(CreateSub(1, "A", 15), CreateSub(2, "B", 15));

        var result = simulator.Simulate(fc, settings, DateTimeOffset.UnixEpoch);

        Assert.Equal(2, result.PerSubResults.Count);
        Assert.All(result.PerSubResults, r => Assert.True(r.VoyageCount > 0));
    }

    [Fact]
    public void UnknownCurrentVoyageCanBlockSimulation()
    {
        var simulator = CreateSimulator();
        var settings = EtaSettings.CreateDefault();
        settings.SimulationMode = SimulationMode.OptimisticPerSub;
        settings.UnknownCurrentVoyagePolicy = UnknownCurrentVoyagePolicy.BlockSimulation;
        var sub = CreateSub(rank: 10) with
        {
            ReturnAtUtc = DateTimeOffset.UnixEpoch.AddDays(1),
            CurrentVoyageKnown = false,
        };
        var fc = CreateFc(sub);

        var result = simulator.Simulate(fc, settings, DateTimeOffset.UnixEpoch);

        Assert.Contains(result.PerSubResults.Single().Warnings, w => w.Contains("unknown", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, result.PerSubResults.Single().VoyageCount);
        Assert.Empty(result.PerSubResults.Single().CurrentRoute);
    }

    [Fact]
    public void SafetyCapProducesIncompleteResultInsteadOfDone()
    {
        var simulator = CreateSimulator(new ScriptedCatalog(routeExp: 100));
        var settings = EtaSettings.CreateDefault() with
        {
            SimulationMode = SimulationMode.OptimisticPerSub,
            SimulationSafetyVoyageCapPerSubmarine = 1,
        };
        settings.TargetRank = 5;
        var fc = CreateFc(CreateSub(rank: 1));

        var result = simulator.Simulate(fc, settings, DateTimeOffset.UnixEpoch);
        var sub = Assert.Single(result.PerSubResults);

        Assert.False(result.IsComplete);
        Assert.False(sub.IsComplete);
        Assert.True(sub.FinalRank < settings.TargetRank);
        Assert.Contains("stopped", sub.IncompleteReason ?? string.Empty);
    }

    [Fact]
    public void PracticalModeRejectsRoutesOverPracticalCap()
    {
        var catalog = new ScriptedCatalog(routeDuration: TimeSpan.FromHours(30));
        var selector = new RouteSelector(catalog, new RouteUnlockGraph(catalog));
        var settings = EtaSettings.CreateDefault() with
        {
            EtaModel = EtaModel.PracticalLeveling,
            PracticalMaxVoyageHours = 24,
        };
        var state = new UnlockState([99], [99], [], []);

        var route = selector.SelectNextRoute(CreateSub(rank: 10), state, catalog.ResolveBuild("TEST", 10), settings, fleetMode: false);

        Assert.Empty(route.Route);
    }

    [Fact]
    public void ExactModePreservesUncappedRouteSearch()
    {
        var catalog = new ScriptedCatalog(routeDuration: TimeSpan.FromHours(30));
        var selector = new RouteSelector(catalog, new RouteUnlockGraph(catalog));
        var settings = EtaSettings.CreateDefault() with
        {
            EtaModel = EtaModel.ExactRouteSearch,
            DurationLimitHours = 0,
        };
        var state = new UnlockState([99], [99], [], []);

        var route = selector.SelectNextRoute(CreateSub(rank: 10), state, catalog.ResolveBuild("TEST", 10), settings, fleetMode: false);

        Assert.Equal([99u], route.Route);
        Assert.False(route.DurationCapApplied);
    }

    [Fact]
    public void RecommendedModeOptimizesExpectedExpPerHour()
    {
        var catalog = new MultiRouteCatalog(
        [
            new RouteCandidate([32u, 34u], 100, TimeSpan.FromHours(2), 50, [], EtaModel.PracticalLeveling, true),
            new RouteCandidate([61u], 1_000, TimeSpan.FromHours(24), 41.7, [], EtaModel.PracticalLeveling, true),
        ]);
        var selector = new RouteSelector(catalog, new RouteUnlockGraph(catalog));
        var settings = EtaSettings.CreateDefault() with
        {
            EtaModel = EtaModel.PracticalLeveling,
            OptimizeExpPerHour = true,
        };
        var state = new UnlockState([32, 34, 61], [32, 34, 61], [], []);

        var route = selector.SelectNextRoute(CreateSub(rank: 75), state, catalog.ResolveBuild("SSUW", 75), settings, fleetMode: false);

        Assert.Equal([32u, 34u], route.Route);
    }

    [Fact]
    public void ExactModeCanStillOptimizeExpPerHour()
    {
        var catalog = new MultiRouteCatalog(
        [
            new RouteCandidate([32u, 34u], 100, TimeSpan.FromHours(2), 50, [], EtaModel.ExactRouteSearch, false),
            new RouteCandidate([61u], 1_000, TimeSpan.FromHours(24), 41.7, [], EtaModel.ExactRouteSearch, false),
        ]);
        var selector = new RouteSelector(catalog, new RouteUnlockGraph(catalog));
        var settings = EtaSettings.CreateDefault() with
        {
            EtaModel = EtaModel.ExactRouteSearch,
            OptimizeExpPerHour = true,
        };
        var state = new UnlockState([32, 34, 61], [32, 34, 61], [], []);

        var route = selector.SelectNextRoute(CreateSub(rank: 75), state, catalog.ResolveBuild("SSUW", 75), settings, fleetMode: false);

        Assert.Equal([32u, 34u], route.Route);
    }

    [Fact]
    public void PracticalOptimisticSimulationBatchesWhenNoUnlockObjectiveIsReachable()
    {
        var simulator = CreateSimulator(new ScriptedCatalog(routeExp: 25, routeDuration: TimeSpan.FromHours(1)));
        var settings = EtaSettings.CreateDefault() with
        {
            SimulationMode = SimulationMode.OptimisticPerSub,
            EtaModel = EtaModel.PracticalLeveling,
            CollectionDelayMinutes = 0,
        };
        settings.TargetRank = 2;
        var fc = CreateFc(new HashSet<uint>([99]), CreateSub(rank: 1) with { NextLevelExp = 100 });

        var result = simulator.Simulate(fc, settings, DateTimeOffset.UnixEpoch);
        var sub = Assert.Single(result.PerSubResults);

        Assert.True(sub.IsComplete);
        Assert.Equal(4, sub.VoyageCount);
        var preview = Assert.Single(sub.VoyagePreview);
        Assert.Equal(4, preview.RepeatCount);
        Assert.Null(preview.UnlockObjective);
        Assert.Equal(TimeSpan.FromHours(4), sub.Remaining);
        Assert.Contains(preview.Warnings, warning => warning.Contains("Batched", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FleetForecastIncludesEveryPlanThroughTargetRank()
    {
        var simulator = CreateSimulator(new ScriptedCatalog(routeExp: 100, routeDuration: TimeSpan.FromDays(1)));
        var settings = EtaSettings.CreateDefault() with
        {
            SimulationMode = SimulationMode.Fleet,
            MaxPreviewVoyagesPerSubmarine = 2,
            CollectionDelayMinutes = 0,
        };
        settings.TargetRank = 5;
        var fc = CreateFc(new HashSet<uint>([99]), CreateSub(rank: 1) with { NextLevelExp = 100 });

        var result = simulator.Simulate(fc, settings, DateTimeOffset.UnixEpoch);
        var sub = Assert.Single(result.PerSubResults);

        Assert.True(sub.IsComplete);
        Assert.Equal(4, sub.VoyageCount);
        Assert.Equal(4, sub.VoyagePreview.Count);
        Assert.Equal(TimeSpan.FromDays(4), sub.Remaining);
        Assert.Equal(TimeSpan.FromDays(4), result.FcCompletionAtUtc - result.GeneratedAtUtc);
    }

    [Fact]
    public void OptimisticForecastIncludesEveryPlanThroughTargetRank()
    {
        var simulator = CreateSimulator(new ScriptedCatalog(routeExp: 100, routeDuration: TimeSpan.FromDays(1)));
        var settings = EtaSettings.CreateDefault() with
        {
            SimulationMode = SimulationMode.OptimisticPerSub,
            EtaModel = EtaModel.ExactRouteSearch,
            MaxPreviewVoyagesPerSubmarine = 2,
            CollectionDelayMinutes = 0,
        };
        settings.TargetRank = 5;
        var fc = CreateFc(new HashSet<uint>([99]), CreateSub(rank: 1) with { NextLevelExp = 100 });

        var result = simulator.Simulate(fc, settings, DateTimeOffset.UnixEpoch);
        var sub = Assert.Single(result.PerSubResults);

        Assert.True(sub.IsComplete);
        Assert.Equal(4, sub.VoyageCount);
        Assert.Equal(4, sub.VoyagePreview.Count);
        Assert.Equal(TimeSpan.FromDays(4), sub.Remaining);
    }

    [Fact]
    public void AlreadyCompletedSubmarineHasZeroEtaEvenWhileAway()
    {
        var simulator = CreateSimulator();
        var settings = EtaSettings.CreateDefault();
        settings.TargetRank = 80;
        var now = DateTimeOffset.UnixEpoch;
        var sub = CreateSub(rank: settings.TargetRank) with
        {
            ReturnAtUtc = now.AddDays(2),
            CurrentRoute = [1],
            CurrentVoyageKnown = true,
        };

        var result = simulator.Simulate(CreateFc(sub), settings, now);
        var subResult = Assert.Single(result.PerSubResults);

        Assert.True(subResult.IsComplete);
        Assert.Equal(now, subResult.EtaAtUtc);
        Assert.Equal(TimeSpan.Zero, subResult.Remaining);
        Assert.Equal(0, subResult.VoyageCount);
    }

    [Fact]
    public void UnlockEligibilityUsesSourceRankInsteadOfTargetRank()
    {
        var catalog = new ScriptedCatalog(
        [
            new UnlockRule(1, 2, SourceRequiredRank: 1, TargetRequiredRank: 50, IsMainProgression: true),
        ]);
        var selector = new RouteSelector(catalog, new RouteUnlockGraph(catalog));
        var state = new UnlockState([1], [1], [], []) { KnownSubmarineSlots = 4 };

        var route = selector.SelectNextRoute(
            CreateSub(rank: 1),
            state,
            catalog.ResolveBuild("TEST", 1),
            EtaSettings.CreateDefault(),
            fleetMode: false);

        Assert.Equal([1u], route.Route);
    }

    [Fact]
    public void MainProgressionIncludesUnflaggedPrerequisiteSectors()
    {
        var catalog = new ScriptedCatalog(
        [
            new UnlockRule(1, 2, 1, 1),
            new UnlockRule(2, 3, 1, 1, IsMainProgression: true),
        ]);
        var selector = new RouteSelector(catalog, new RouteUnlockGraph(catalog));
        var state = new UnlockState([1], [1], [], []) { KnownSubmarineSlots = 4 };

        var route = selector.SelectNextRoute(
            CreateSub(rank: 1),
            state,
            catalog.ResolveBuild("TEST", 1),
            EtaSettings.CreateDefault(),
            fleetMode: false);

        Assert.Equal([1u], route.Route);
    }

    [Fact]
    public void MainProgressionUnlocksEarlierSiblingBeforeMainTarget()
    {
        var catalog = new ScriptedCatalog(
        [
            new UnlockRule(1, 2, 1, 1),
            new UnlockRule(1, 3, 1, 1, IsMainProgression: true),
        ]);
        var graph = new RouteUnlockGraph(catalog);
        var settings = EtaSettings.CreateDefault();
        var state = new UnlockState([1], [1], [], []) { KnownSubmarineSlots = 4 };

        var sibling = graph.GetNextObjective(state, settings, rank: 1, targetRank: 3, fleetMode: false);
        Assert.NotNull(sibling);
        Assert.Equal(2u, sibling.TargetPoint);

        state.UnlockedPoints.Add(2);
        var mainTarget = graph.GetNextObjective(state, settings, rank: 1, targetRank: 3, fleetMode: false);
        Assert.NotNull(mainTarget);
        Assert.Equal(3u, mainTarget.TargetPoint);
    }

    [Fact]
    public void MainProgressionResolvesChainedSiblingGatesInCatalogOrder()
    {
        var catalog = new ScriptedCatalog(
        [
            new UnlockRule(1, 2, 1, 1),
            new UnlockRule(1, 3, 1, 1, IsMainProgression: true),
            new UnlockRule(3, 4, 1, 1),
            new UnlockRule(3, 5, 1, 1, IsMainProgression: true),
        ]);
        var graph = new RouteUnlockGraph(catalog);
        var settings = EtaSettings.CreateDefault();
        var state = new UnlockState([1], [1], [], []) { KnownSubmarineSlots = 4 };
        var objectives = new List<uint>();

        for (var i = 0; i < 4; i++)
        {
            var objective = graph.GetNextObjective(state, settings, rank: 1, targetRank: 5, fleetMode: false);
            Assert.NotNull(objective);
            objectives.Add(objective.TargetPoint);
            state.UnlockedPoints.Add(objective.TargetPoint);
        }

        Assert.Equal([2u, 3u, 4u, 5u], objectives);
    }

    [Fact]
    public void SaturatedSiblingMakesRemainingSubmarinesUseLevelingRoutes()
    {
        var catalog = new ScriptedCatalog(
        [
            new UnlockRule(1, 2, 1, 1),
            new UnlockRule(1, 3, 1, 1, IsMainProgression: true),
        ]);
        var graph = new RouteUnlockGraph(catalog);
        var selector = new RouteSelector(catalog, graph);
        var settings = EtaSettings.CreateDefault() with { UnlockSuccessProbability = 1.0 };
        var state = new UnlockState([1, 99], [1, 99], [2], []) { KnownSubmarineSlots = 4 };
        state.PendingUnlockAttempts[2] = 1;

        var route = selector.SelectNextRoute(
            CreateSub(rank: 1),
            state,
            catalog.ResolveBuild("TEST", 1),
            settings,
            fleetMode: true);

        Assert.Equal([99u], route.Route);
        Assert.DoesNotContain(3u, route.UnlockTargets);
    }

    [Fact]
    public void MainProgressionHandlesKnownLaterMapSiblingChains()
    {
        var catalog = new ScriptedCatalog(
        [
            new UnlockRule(49, 53, 1, 1, IsMainProgression: true),
            new UnlockRule(53, 54, 1, 1),
            new UnlockRule(53, 55, 1, 1, IsMainProgression: true),
            new UnlockRule(55, 56, 1, 1),
            new UnlockRule(55, 57, 1, 1, IsMainProgression: true),
        ]);
        var graph = new RouteUnlockGraph(catalog);
        var settings = EtaSettings.CreateDefault();
        var state = new UnlockState([49, 53], [49, 53], [], []) { KnownSubmarineSlots = 4 };
        var objectives = new List<uint>();

        for (var i = 0; i < 4; i++)
        {
            var objective = graph.GetNextObjective(state, settings, rank: 70, targetRank: 114, fleetMode: false);
            Assert.NotNull(objective);
            objectives.Add(objective.TargetPoint);
            state.UnlockedPoints.Add(objective.TargetPoint);
        }

        Assert.Equal([54u, 55u, 56u, 57u], objectives);
    }

    [Fact]
    public void CotedShapedForecastProgressesBeyondSecondMapAfterSiblingUnlocks()
    {
        var catalog = new ScriptedCatalog(
        [
            new UnlockRule(49, 53, 1, 1, IsMainProgression: true),
            new UnlockRule(53, 54, 1, 1),
            new UnlockRule(53, 55, 1, 1, IsMainProgression: true),
            new UnlockRule(55, 56, 1, 1),
            new UnlockRule(55, 57, 1, 1, IsMainProgression: true),
        ]);
        var simulator = CreateSimulator(catalog);
        var settings = EtaSettings.CreateDefault() with
        {
            TargetRank = 75,
            UnlockSuccessProbability = 1.0,
            SimulationSafetyVoyageCapPerSubmarine = 10,
        };
        var fc = CreateFc(new HashSet<uint>([49, 53]), CreateSub(rank: 70));

        var result = simulator.Simulate(fc, settings, DateTimeOffset.UnixEpoch);
        var unlocked = result.UnlockMilestones
            .Where(milestone => milestone.Kind == UnlockMilestoneKind.SectorUnlocked)
            .Select(milestone => milestone.UnlockedPoint)
            .ToArray();

        Assert.True(result.IsComplete);
        Assert.Equal([54u, 55u, 56u, 57u], unlocked);
        Assert.Contains(result.PlannedRoutes, route => route.Route.Contains(55u));
    }

    [Fact]
    public void SubmarineSlotPathUnlocksEarlierSiblingBeforeSlotTarget()
    {
        var catalog = new ScriptedCatalog(
        [
            new UnlockRule(1, 2, 1, 1),
            new UnlockRule(1, 3, 1, 1, UnlocksSubSlot: true),
        ]);
        var graph = new RouteUnlockGraph(catalog);
        var settings = EtaSettings.CreateDefault() with
        {
            RouteGoal = RouteGoal.UnlockSubSlotsThenLevel,
            PrioritizeSubSlots = true,
        };
        var state = new UnlockState([1], [1], [], []) { KnownSubmarineSlots = 1 };

        var sibling = graph.GetNextObjective(state, settings, rank: 1, targetRank: 10, fleetMode: false);
        Assert.NotNull(sibling);
        Assert.Equal(2u, sibling.TargetPoint);

        state.UnlockedPoints.Add(2);
        var slotTarget = graph.GetNextObjective(state, settings, rank: 1, targetRank: 10, fleetMode: false);
        Assert.NotNull(slotTarget);
        Assert.Equal(3u, slotTarget.TargetPoint);
    }

    [Fact]
    public void SubmarineSlotRequiresExploringTheUnlockedTarget()
    {
        var catalog = new ScriptedCatalog(
        [
            new UnlockRule(1, 2, 1, 1, UnlocksSubSlot: true, IsMainProgression: true),
        ]);
        var graph = new RouteUnlockGraph(catalog);
        var state = new UnlockState([1], [1], [], []) { KnownSubmarineSlots = 1 };

        graph.MarkRouteReturn([1], state, submarineId: 1, DateTimeOffset.UnixEpoch.AddHours(12));

        Assert.Contains(2u, state.UnlockedPoints);
        Assert.DoesNotContain(2u, state.ExploredPoints);
        Assert.Equal(1, state.KnownSubmarineSlots);
        Assert.DoesNotContain(state.UnlockMilestones, item => item.Kind == UnlockMilestoneKind.SubmarineSlotUnlocked);

        graph.MarkRouteReturn([2], state, submarineId: 1, DateTimeOffset.UnixEpoch.AddHours(24));

        Assert.Contains(2u, state.ExploredPoints);
        Assert.Equal(2, state.KnownSubmarineSlots);
        Assert.Contains(state.UnlockMilestones, item => item.Kind == UnlockMilestoneKind.SubmarineSlotUnlocked);
    }

    [Fact]
    public void FleetUnlockIsNotVisibleBeforeTheUnlockingVoyageReturns()
    {
        var catalog = new ScriptedCatalog(
        [
            new UnlockRule(1, 2, 1, 1, IsMainProgression: true),
        ], routeDuration: TimeSpan.FromHours(1));
        var simulator = CreateSimulator(catalog);
        var settings = EtaSettings.CreateDefault() with { SimulationMode = SimulationMode.Fleet };
        settings.TargetRank = 2;
        var delayedSub = CreateSub(2, "B", 1) with
        {
            ReturnAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(30),
            CurrentVoyageKnown = false,
        };
        var fc = CreateFc(new HashSet<uint>([1]), CreateSub(1, "A", 1), delayedSub);

        simulator.Simulate(fc, settings, DateTimeOffset.UnixEpoch);

        Assert.True(catalog.ObservedUnlockedStates.Count >= 2);
        Assert.DoesNotContain(2u, catalog.ObservedUnlockedStates[1]);
    }

    [Fact]
    public void FailedUnlockRollLeavesTargetLocked()
    {
        var catalog = new ScriptedCatalog([new UnlockRule(1, 2, 1, 1)]);
        var graph = new RouteUnlockGraph(catalog);
        var state = new UnlockState([1], [1], [], []);

        var unlocked = graph.MarkRouteReturn([1], state, 1, DateTimeOffset.UnixEpoch, _ => false);

        Assert.Empty(unlocked);
        Assert.DoesNotContain(2u, state.UnlockedPoints);
    }

    [Fact]
    public void SourceWithMultipleTargetsUnlocksInCatalogOrder()
    {
        var catalog = new ScriptedCatalog([
            new UnlockRule(1, 3, 1, 1),
            new UnlockRule(1, 2, 1, 1),
        ]);
        var graph = new RouteUnlockGraph(catalog);
        var state = new UnlockState([1], [1], [], []);

        graph.MarkRouteReturn([1], state, 1, DateTimeOffset.UnixEpoch, _ => true);
        Assert.Contains(2u, state.UnlockedPoints);
        Assert.DoesNotContain(3u, state.UnlockedPoints);

        graph.MarkRouteReturn([1], state, 1, DateTimeOffset.UnixEpoch.AddHours(1), _ => true);
        Assert.Contains(3u, state.UnlockedPoints);
    }

    [Fact]
    public void ThirtyThreePercentPolicyAllowsTwoConcurrentUnlockAttempts()
    {
        var catalog = new ScriptedCatalog([new UnlockRule(1, 2, 1, 1, IsMainProgression: true)]);
        var graph = new RouteUnlockGraph(catalog);
        var selector = new RouteSelector(catalog, graph);
        var settings = EtaSettings.CreateDefault() with { UnlockSuccessProbability = 0.33 };
        var state = new UnlockState([1], [1], [], []);
        var sub = CreateSub(rank: 1);
        var build = new BuildResolver(catalog).ResolveBuildForRank(1, settings);

        var first = selector.SelectNextRoute(sub, state, build, settings, fleetMode: true);
        var second = selector.SelectNextRoute(sub, state, build, settings, fleetMode: true);
        var third = selector.SelectNextRoute(sub, state, build, settings, fleetMode: true);

        Assert.Contains(2u, first.UnlockTargets);
        Assert.Contains(2u, second.UnlockTargets);
        Assert.DoesNotContain(2u, third.UnlockTargets);
    }

    [Fact]
    public void ProbabilityForecastReportsOrderedRangeAndActiveFleetAttempts()
    {
        var catalog = new ScriptedCatalog([new UnlockRule(1, 2, 1, 1)]);
        var simulator = CreateSimulator(catalog);
        var settings = EtaSettings.CreateDefault() with { TargetRank = 1, UnlockSuccessProbability = 0.33 };
        var returnAt = DateTimeOffset.UnixEpoch.AddHours(6);
        var first = CreateSub(1, "A", 1) with { ReturnAtUtc = returnAt, CurrentRoute = [1], CurrentVoyageKnown = true };
        var second = CreateSub(2, "B", 1) with { ReturnAtUtc = returnAt.AddMinutes(5), CurrentRoute = [1], CurrentVoyageKnown = true };
        var fc = CreateFc(new HashSet<uint>([1]), first, second);

        var result = simulator.Simulate(fc, settings, DateTimeOffset.UnixEpoch);
        var attempt = Assert.Single(result.ActiveUnlockAttempts);

        Assert.NotNull(result.CompletionForecast);
        Assert.True(result.CompletionForecast.P10AtUtc <= result.CompletionForecast.P50AtUtc);
        Assert.True(result.CompletionForecast.P50AtUtc <= result.CompletionForecast.P90AtUtc);
        Assert.InRange(result.ProbabilitySampleCount, 64, 256);
        Assert.Equal(2, attempt.SubmarineIds.Count);
        Assert.Equal(1 - Math.Pow(0.67, 2), attempt.CombinedSuccessProbability, 6);
    }

    [Fact]
    public void ProbabilityForecastIsDeterministicAndExposesConditionalNextRoutes()
    {
        var catalog = new ScriptedCatalog([new UnlockRule(1, 2, 1, 1, IsMainProgression: true)]);
        var simulator = CreateSimulator(catalog);
        var settings = EtaSettings.CreateDefault() with { TargetRank = 3, UnlockSuccessProbability = 0.33 };
        var sub = CreateSub(rank: 1) with
        {
            ReturnAtUtc = DateTimeOffset.UnixEpoch.AddHours(1),
            CurrentRoute = [1],
            CurrentVoyageKnown = true,
        };
        var fc = CreateFc(new HashSet<uint>([1]), sub);

        var first = simulator.Simulate(fc, settings, DateTimeOffset.UnixEpoch);
        var second = simulator.Simulate(fc, settings, DateTimeOffset.UnixEpoch);
        var firstSub = Assert.Single(first.PerSubResults);
        var secondSub = Assert.Single(second.PerSubResults);

        Assert.Equal(first.CompletionForecast, second.CompletionForecast);
        Assert.Equal(firstSub.EtaForecast, secondSub.EtaForecast);
        Assert.Equal(
            firstSub.NextRouteOutcomes.Select(outcome => (string.Join(",", outcome.Route), outcome.Probability)),
            secondSub.NextRouteOutcomes.Select(outcome => (string.Join(",", outcome.Route), outcome.Probability)));
        Assert.True(firstSub.NextRouteOutcomes.Count >= 2);
        Assert.Equal(1.0, firstSub.NextRouteOutcomes.Sum(outcome => outcome.Probability), 6);
        Assert.Equal(
            firstSub.NextRouteOutcomes.Max(outcome => outcome.Probability),
            firstSub.NextRouteOutcomes[0].Probability);
    }

    [Fact]
    public void ProbabilityForecastIsPartialWhenMinimumSamplesCannotComplete()
    {
        var simulator = CreateSimulator();
        var settings = EtaSettings.CreateDefault() with { TargetRank = 2 };
        var result = simulator.Simulate(
            CreateFc(new HashSet<uint>([1]), CreateSub(rank: 1)),
            settings,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UtcNow.AddMilliseconds(-1),
            CancellationToken.None);

        Assert.False(result.IsComplete);
        Assert.Equal(0, result.ProbabilitySampleCount);
        Assert.Contains(result.Warnings, warning => warning.Contains("minimum probability samples"));
    }

    [Fact]
    public void MissingUnlockDataProducesIncompleteEta()
    {
        var simulator = CreateSimulator();
        var settings = EtaSettings.CreateDefault();
        var fc = CreateFc(new HashSet<uint>(), CreateSub(rank: 10)) with { UnlockDataKnown = false };

        var result = simulator.Simulate(fc, settings, DateTimeOffset.UnixEpoch);

        Assert.False(result.IsComplete);
        Assert.Contains(result.PerSubResults, sub => !sub.IsComplete);
    }

    [Fact]
    public void ResultFiltersUseCurrentRanksAndExpansionCommandsAreOneShot()
    {
        var simulator = CreateSimulator();
        var settings = EtaSettings.CreateDefault();
        settings.TargetRank = 80;
        var ready = simulator.Simulate(CreateFc(CreateSub(rank: settings.TargetRank)), settings, DateTimeOffset.UnixEpoch);
        var leveling = simulator.Simulate(CreateFc(CreateSub(rank: settings.TargetRank - 1)), settings, DateTimeOffset.UnixEpoch);
        var viewState = new ResultsViewState();

        Assert.True(ResultsViewState.ShouldInclude(leveling, settings.TargetRank, FcResultFilter.Leveling));
        Assert.False(ResultsViewState.ShouldInclude(ready, settings.TargetRank, FcResultFilter.Leveling));
        Assert.True(ResultsViewState.ShouldInclude(ready, settings.TargetRank, FcResultFilter.Ready));

        viewState.ExpandAll();
        Assert.True(viewState.ExpansionOverride);
        viewState.ClearExpansionOverride();
        Assert.Null(viewState.ExpansionOverride);
        viewState.CollapseAll();
        Assert.False(viewState.ExpansionOverride);
    }

    [Theory]
    [InlineData(null, "Median 12d")]
    [InlineData(FcCalculationStatus.Reused, "Median 12d")]
    [InlineData(FcCalculationStatus.Complete, "Median 12d")]
    [InlineData(FcCalculationStatus.Partial, "Median 12d")]
    [InlineData(FcCalculationStatus.Cancelled, "Median 12d")]
    [InlineData(FcCalculationStatus.Queued, "Queued for refresh")]
    [InlineData(FcCalculationStatus.Calculating, "Refreshing")]
    [InlineData(FcCalculationStatus.AwaitingTrackerUpdate, "Waiting for SubmarineTracker")]
    [InlineData(FcCalculationStatus.TimedOut, "Timed out")]
    [InlineData(FcCalculationStatus.Failed, "Refresh failed")]
    public void CollapsedResultStatusOnlyYieldsToActionableCalculationStates(
        FcCalculationStatus? calculationStatus,
        string expected)
    {
        var calculationText = calculationStatus switch
        {
            FcCalculationStatus.Queued => "Queued for refresh",
            FcCalculationStatus.Calculating => "Refreshing",
            FcCalculationStatus.AwaitingTrackerUpdate => "Waiting for SubmarineTracker",
            FcCalculationStatus.TimedOut => "Timed out",
            FcCalculationStatus.Failed => "Refresh failed",
            FcCalculationStatus.Reused => "Up to date",
            _ => "Calculation complete",
        };

        var selected = ResultsViewState.SelectCollapsedStatus(
            "Median 12d",
            calculationStatus,
            calculationText);

        Assert.Equal(expected, selected);
    }

    [Fact]
    public void ReusedReadyResultKeepsReadyNowAsCollapsedStatus()
    {
        var selected = ResultsViewState.SelectCollapsedStatus(
            "Ready now",
            FcCalculationStatus.Reused,
            "Up to date");

        Assert.Equal("Ready now", selected);
    }

    [Theory]
    [InlineData("Ready now")]
    [InlineData("Median 12d")]
    [InlineData("Up to date")]
    [InlineData("Calculating 00:05")]
    [InlineData("Incomplete")]
    [InlineData("Queued")]
    public void CollapsedFcStatusesAppendRecordedSalvage(string status)
    {
        Assert.Equal(
            $"{status} • Salvage 12m gil",
            ResultsViewState.FormatCollapsedHeaderStatus(status, 12_000_000));
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1_000, "1k")]
    [InlineData(12_000_000, "12m")]
    [InlineData(1_000_000_000, "1b")]
    public void CompactGilCoversHeaderDisplayRanges(long gil, string expected)
    {
        Assert.Equal(expected, ResultsViewState.FormatCompactGil(gil));
    }

    [Theory]
    [InlineData(4u, "Île d'Anthémuse (D)", "D")]
    [InlineData(8u, "The Wreckage (North) (h)", "H")]
    [InlineData(11u, "The Wreckage (North)", "11")]
    [InlineData(12u, "M11", "M11")]
    [InlineData(13u, "A very long destination", "13")]
    [InlineData(14u, "", "14")]
    public void CompactPointCodesUseTerminalIdentifiersAndSafeFallbacks(uint point, string name, string expected)
    {
        Assert.Equal(expected, RouteDisplayFormatter.ExtractPointCode(point, name));
    }

    [Fact]
    public void CompactRoutesUseLettersAndAnEmDashForNoActiveRoute()
    {
        var names = new Dictionary<uint, string>
        {
            [4] = "Île d'Anthémuse (D)",
            [8] = "Mer du Chant des sirènes 3 (H)",
            [9] = "Courant sous-marin d'Anthémuse (I)",
        };

        Assert.Equal("D → H → I", RouteDisplayFormatter.FormatCompactRoute([4, 8, 9], point => names[point]));
        Assert.Equal("—", RouteDisplayFormatter.FormatCompactRoute([], point => names[point]));
    }

    [Fact]
    public void ManualOverrideIsExposedAsTheEffectiveCurrentRoute()
    {
        var catalog = new ScriptedCatalog();
        var simulator = CreateSimulator(catalog);
        var settings = EtaSettings.CreateDefault() with { TargetRank = 2 };
        var sub = CreateSub(rank: 1) with
        {
            ReturnAtUtc = DateTimeOffset.UnixEpoch.AddHours(6),
            CurrentRoute = [1],
            CurrentVoyageKnown = true,
            ManualCurrentRouteOverride = [7],
        };

        var result = simulator.Simulate(
            CreateFc(new HashSet<uint>([1, 7]), sub),
            settings,
            DateTimeOffset.UnixEpoch);

        Assert.Equal([7u], Assert.Single(result.PerSubResults).CurrentRoute);
    }

    [Fact]
    public void SubmarineVoyageDurationIncludesTwelveHourBaseline()
    {
        var catalog = new CompatSubmarineCatalog();
        var build = catalog.ResolveBuild("SSUW", 75);

        var duration = catalog.CalculateDuration([1], build);

        Assert.True(duration >= TimeSpan.FromHours(12));
    }

    [Fact]
    public void RepoJsonContainsExpectedDownloadLinks()
    {
        var repoJsonPath = FindRepoJson();
        var repoJson = File.ReadAllText(repoJsonPath);

        Assert.Contains("SubmarineEtaPlanner", repoJson);
        Assert.Contains("\"Author\": \"Alex Vallière\"", repoJson);
        Assert.Contains("Estimate submarine ETAs to your chosen rank", repoJson);
        Assert.Contains("Forecast submarine ETAs to a chosen rank", repoJson);
        Assert.Contains("\"AssemblyVersion\": \"0.5.22.0\"", repoJson);
        Assert.Contains("https://github.com/AlexValliere/submarineEtaPlanner", repoJson);
        Assert.Contains("https://alexvalliere.github.io/submarineEtaPlanner/SubmarineEtaPlanner/latest.zip", repoJson);
        Assert.Contains("https://alexvalliere.github.io/submarineEtaPlanner/images/icon.png", repoJson);
        Assert.Contains("Requires Submarine Tracker to be installed and enabled", repoJson);
        Assert.Contains("installer icon was created with AI assistance", repoJson);
        Assert.Contains("Adds read-only fuel-stock models", repoJson);
        Assert.Contains("\"DalamudApiLevel\": 15", repoJson);
    }

    [Fact]
    public void PluginIconIsValidAndPublishedByPagesWorkflow()
    {
        var repositoryRoot = Path.GetDirectoryName(FindRepoJson())!;
        var iconPath = Path.Combine(repositoryRoot, "images", "icon.png");
        var icon = File.ReadAllBytes(iconPath);

        Assert.Equal([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a], icon.Take(8));
        Assert.Equal(512, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(icon.AsSpan(16, 4)));
        Assert.Equal(512, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(icon.AsSpan(20, 4)));

        var workflow = File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "build.yml"));
        Assert.Contains("Copy-Item images/icon.png public/images/icon.png", workflow);
        Assert.DoesNotContain("public/images/icon-", workflow);
        Assert.Contains("Copy-Item \"$out/CalculatedData.msgpack\" $packageDir", workflow);
    }

    [Fact]
    public void IncomeConfigurationDefaultsToFarmingAndPreservesLifetimeValue()
    {
        var repositoryRoot = Path.GetDirectoryName(FindRepoJson())!;
        var configuration = File.ReadAllText(Path.Combine(repositoryRoot, "src", "SubmarineEtaPlanner", "Configuration.cs"));

        Assert.Contains("public IncomeView IncomeView { get; set; } = IncomeViewPreferences.Default;", configuration);
        Assert.Contains("IncomePeriod { Days7 = 0, Days30 = 1, Days90 = 2, Lifetime = 3, Days365 = 4 }", configuration);
    }

    [Fact]
    public void IncomeUiUsesPlainMetricNames()
    {
        var repositoryRoot = Path.GetDirectoryName(FindRepoJson())!;
        var incomePage = File.ReadAllText(Path.Combine(repositoryRoot, "src", "SubmarineEtaPlanner", "Ui", "PlannerWindow.Income.cs"));

        Assert.DoesNotContain("covered day", incomePage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("valid voyage", incomePage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"Recorded avg / day\"", incomePage);
        Assert.Contains("\"Observed run rate\"", incomePage);
        Assert.Contains("not guaranteed income", incomePage);
        Assert.Contains("\"Gil / voyage\"", incomePage);
        Assert.Contains("DrawIncomePeriodButton(\"1 year\", IncomePeriod.Days365)", incomePage);
    }

    [Fact]
    public void PublicReleaseAssetsIncludeLicensesAndVerifiedRouteData()
    {
        var repositoryRoot = Path.GetDirectoryName(FindRepoJson())!;
        var license = File.ReadAllText(Path.Combine(repositoryRoot, "LICENSE"));
        var notices = File.ReadAllText(Path.Combine(repositoryRoot, "THIRD_PARTY_NOTICES.md"));
        var aiDisclosure = File.ReadAllText(Path.Combine(repositoryRoot, "AI_USAGE.md"));
        var routeData = File.ReadAllBytes(Path.Combine(
            repositoryRoot,
            "src",
            "SubmarineEtaPlanner",
            "CalculatedData.msgpack"));
        var routeHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(routeData));

        Assert.Contains("Copyright (c) 2026 Alex Vallière", license);
        Assert.Contains("Permission is hereby granted", license);
        Assert.Contains("Copyright (c) 2023 Infi", notices);
        Assert.Contains("aa3b40ce3e7eb9c2db9b5ad4ce2cb489755d7a5a", notices);
        Assert.Contains("Copilot", aiDisclosure);
        Assert.Equal("24996254FAB3FFC4A74F1AFA2C9212732888A0C6387DAB026B75EA566B6D67FF", routeHash);
    }

    private static EtaSimulator CreateSimulator(ISubmarineCatalog? catalog = null)
    {
        catalog ??= new CompatSubmarineCatalog();
        var buildResolver = new BuildResolver(catalog);
        var unlockGraph = new RouteUnlockGraph(catalog);
        var selector = new RouteSelector(catalog, unlockGraph);
        return new EtaSimulator(buildResolver, unlockGraph, selector, catalog);
    }

    private static EtaResult SimulateScoped(
        EtaSimulator simulator,
        FcState fc,
        EtaSettings settings,
        DateTimeOffset now,
        params long[] targetSubmarineIds)
        => simulator.Simulate(
            fc,
            settings,
            new EtaSimulationScope(targetSubmarineIds.ToHashSet()),
            now,
            deadlineUtc: null,
            CancellationToken.None);

    private static FcState CreateFc(params SubmarineState[] submarines)
        => CreateFc(Enumerable.Range(1, 20).Select(i => (uint)i).ToHashSet(), submarines);

    private static FcState CreateFc(IReadOnlySet<uint> unlockedPoints, params SubmarineState[] submarines)
        => new(
            [1, 2, 3],
            "TEST",
            "World",
            unlockedPoints,
            unlockedPoints,
            submarines);

    private static SubmarineState CreateSub(long id = 1, string name = "Sub", int rank = 1, uint currentExp = 0)
        => new(
            [1, 2, 3],
            id,
            name,
            rank,
            currentExp,
            1000,
            SubmarineBuildParts.Empty,
            DateTimeOffset.UnixEpoch,
            [],
            true,
            []);

    private static string FindRepoJson()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "repo.json");
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate repo.json from the test output directory.");
    }

    private sealed class ScriptedCatalog : ISubmarineCatalog
    {
        private readonly IReadOnlyList<UnlockRule> unlockRules;
        private readonly uint routeExp;
        private readonly TimeSpan routeDuration;

        public ScriptedCatalog(
            IReadOnlyList<UnlockRule>? unlockRules = null,
            uint routeExp = 100,
            TimeSpan? routeDuration = null)
        {
            this.unlockRules = unlockRules ?? [new UnlockRule(1, 2, 1, 1, IsMainProgression: true)];
            this.routeExp = routeExp;
            this.routeDuration = routeDuration ?? TimeSpan.FromHours(1);
        }

        public IReadOnlyList<UnlockRule> UnlockRules => this.unlockRules;

        public int MaximumRank => 149;

        public IReadOnlyList<uint> LastMustInclude { get; private set; } = [];

        public int PartBuildResolutionCount { get; private set; }

        public List<HashSet<uint>> ObservedUnlockedStates { get; } = [];

        public SubmarineBuild ResolveBuild(string buildCode, int rank)
            => new($"R{rank}", rank, 100, 100, 100, 999, 100);

        public SubmarineBuild? ResolveBuild(SubmarineBuildParts buildParts, int rank)
        {
            PartBuildResolutionCount++;
            return buildParts == SubmarineBuildParts.Empty ? null : new SubmarineBuild($"P{rank}", rank, 100, 100, 100, 999, 100);
        }

        public RouteSearchResult FindBestRoute(RouteSearchRequest request)
        {
            ObservedUnlockedStates.Add(new HashSet<uint>(request.UnlockedPoints));
            LastMustInclude = Enumerable.Range(0, 192)
                .Select(point => (uint)point)
                .Where(request.MustIncludeMask.Contains)
                .ToArray();
            var route = LastMustInclude.Count > 0 ? [LastMustInclude[0]] : new uint[] { 99 };
            if (route.Any(request.ExcludedSectorMask.Contains))
                return new RouteSearchResult(null, 1, CacheHit: false);
            var durationLimitHours = request.Settings.GetEffectiveDurationLimitHours();
            if (durationLimitHours > 0 && this.routeDuration > TimeSpan.FromHours(durationLimitHours))
                return new RouteSearchResult(null, 1, CacheHit: false);

            var unlockTargets = this.unlockRules
                .Where(rule => route.Contains(rule.SourcePoint))
                .Select(rule => rule.UnlocksPoint)
                .ToArray();

            return new RouteSearchResult(new RouteCandidate(
                route,
                this.routeExp,
                this.routeDuration,
                this.routeExp / Math.Max(this.routeDuration.TotalHours, 0.01),
                unlockTargets,
                request.Settings.EtaModel,
                durationLimitHours > 0), 1, CacheHit: false);
        }

        public uint CalculateExp(IReadOnlyList<uint> route, SubmarineBuild build, ExpMode expMode) => this.routeExp;

        public TimeSpan CalculateDuration(IReadOnlyList<uint> route, SubmarineBuild build) => this.routeDuration;

        public (int Rank, uint CurrentExp, uint NextLevelExp) ApplyExp(int rank, uint currentExp, uint gainedExp, int targetRank)
        {
            var total = currentExp + gainedExp;
            while (rank < targetRank && total >= 100)
            {
                total -= 100;
                rank++;
            }

            return (rank, rank >= targetRank ? 0 : total, rank >= targetRank ? 0u : 100u);
        }

        public string PointName(uint point) => point.ToString();

        public int GetPointRequiredRank(uint point) => 1;
    }

    private sealed class MultiRouteCatalog(IReadOnlyList<RouteCandidate> routes) : ISubmarineCatalog
    {
        public IReadOnlyList<UnlockRule> UnlockRules => [];

        public int MaximumRank => 149;

        public SubmarineBuild ResolveBuild(string buildCode, int rank)
            => new(buildCode, rank, 100, 100, 100, 999, 100);

        public SubmarineBuild? ResolveBuild(SubmarineBuildParts buildParts, int rank)
            => ResolveBuild("TEST", rank);

        public RouteSearchResult FindBestRoute(RouteSearchRequest request)
        {
            var selected = routes
                .Where(route => request.MustIncludeMask.IsEmpty || SectorMask.From(route.Route).Intersects(request.MustIncludeMask))
                .Where(route => !SectorMask.From(route.Route).Intersects(request.ExcludedSectorMask))
                .Where(route => request.Settings.GetEffectiveDurationLimitHours() <= 0 ||
                                route.Duration <= TimeSpan.FromHours(request.Settings.GetEffectiveDurationLimitHours()))
                .OrderByDescending(route => request.Settings.GetEffectiveOptimizeExpPerHour() ? route.ExpPerHour : route.Exp)
                .ThenBy(route => route.Duration)
                .FirstOrDefault();
            return new RouteSearchResult(selected, routes.Count, CacheHit: false);
        }

        public uint CalculateExp(IReadOnlyList<uint> route, SubmarineBuild build, ExpMode expMode) => 0;

        public TimeSpan CalculateDuration(IReadOnlyList<uint> route, SubmarineBuild build) => TimeSpan.Zero;

        public (int Rank, uint CurrentExp, uint NextLevelExp) ApplyExp(int rank, uint currentExp, uint gainedExp, int targetRank)
            => (rank, currentExp, 100);

        public string PointName(uint point) => point.ToString();

        public int GetPointRequiredRank(uint point) => 1;
    }
}
