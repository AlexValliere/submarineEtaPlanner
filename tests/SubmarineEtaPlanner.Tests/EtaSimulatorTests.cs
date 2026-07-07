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

        var result = catalog.ApplyExp(10, 0, 100_000, 114);

        Assert.True(result.Rank > 10);
        Assert.True(result.CurrentExp < result.NextLevelExp || result.NextLevelExp == 0);
    }

    [Fact]
    public void StopsAtTargetRank()
    {
        var simulator = CreateSimulator();
        var settings = EtaSettings.CreateDefault();
        var fc = CreateFc(CreateSub(rank: 113, currentExp: 500_000));

        var result = simulator.Simulate(fc, settings, DateTimeOffset.UnixEpoch);

        Assert.All(result.PerSubResults, sub => Assert.True(sub.FinalRank >= 114));
        Assert.DoesNotContain(result.PlannedRoutes, p => p.RankBefore >= 114);
    }

    [Fact]
    public void FleetModeSharesUnlockState()
    {
        var simulator = CreateSimulator();
        var settings = EtaSettings.CreateDefault() with { SimulationMode = SimulationMode.Fleet, EtaModel = EtaModel.ExactRouteSearch };
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
        var settings = EtaSettings.CreateDefault() with { SimulationMode = SimulationMode.Fleet, EtaModel = EtaModel.ExactRouteSearch };
        settings.TargetRank = 22;
        var fc = CreateFc(Enumerable.Range(1, 15).Select(i => (uint)i).ToHashSet(), CreateSub(1, "A", 20));

        var result = simulator.Simulate(fc, settings, DateTimeOffset.UnixEpoch);
        var milestone = Assert.Single(result.UnlockMilestones);
        var unlockPlan = Assert.Single(result.PlannedRoutes, plan => plan.UnlocksApplied.Contains(milestone.UnlockedPoint));

        Assert.Equal(unlockPlan.ReturnAtUtc, milestone.ReturnAtUtc);
    }

    [Fact]
    public void RouteSelectorAvoidsDuplicatePendingUnlocks()
    {
        var catalog = new CompatSubmarineCatalog();
        var unlockGraph = new RouteUnlockGraph(catalog);
        var selector = new RouteSelector(catalog, unlockGraph);
        var settings = EtaSettings.CreateDefault() with { SimulationMode = SimulationMode.Fleet };
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
        var settings = EtaSettings.CreateDefault() with { SimulationMode = SimulationMode.Fleet };
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
        Assert.Contains(result.UnlockMilestones, milestone => milestone.UnlockedPoint == 8);
        Assert.Equal(1, catalog.PartBuildResolutionCount);
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
    public void PracticalModeOptimizesRouteExpInsteadOfExpPerHour()
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

        Assert.Equal([61u], route.Route);
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
    public void PracticalOptimisticSimulationDoesNotBatchWhenUnlockProgressionIsActive()
    {
        var simulator = CreateSimulator(new ScriptedCatalog(routeExp: 25, routeDuration: TimeSpan.FromHours(1)));
        var settings = EtaSettings.CreateDefault() with
        {
            SimulationMode = SimulationMode.OptimisticPerSub,
            EtaModel = EtaModel.PracticalLeveling,
        };
        settings.TargetRank = 2;
        var fc = CreateFc(new HashSet<uint>([99]), CreateSub(rank: 1) with { NextLevelExp = 100 });

        var result = simulator.Simulate(fc, settings, DateTimeOffset.UnixEpoch);
        var sub = Assert.Single(result.PerSubResults);

        Assert.True(sub.IsComplete);
        Assert.Equal(4, sub.VoyageCount);
        Assert.Equal(4, sub.VoyagePreview.Count);
        Assert.Equal(TimeSpan.FromHours(4), sub.Remaining);
        Assert.All(sub.VoyagePreview, plan => Assert.Empty(plan.Warnings));
    }

    [Fact]
    public void FleetEtaUsesFinalAvailabilityWhenPreviewIsCapped()
    {
        var simulator = CreateSimulator(new ScriptedCatalog(routeExp: 100, routeDuration: TimeSpan.FromDays(1)));
        var settings = EtaSettings.CreateDefault() with
        {
            SimulationMode = SimulationMode.Fleet,
            MaxPreviewVoyagesPerSubmarine = 2,
        };
        settings.TargetRank = 5;
        var fc = CreateFc(new HashSet<uint>([99]), CreateSub(rank: 1) with { NextLevelExp = 100 });

        var result = simulator.Simulate(fc, settings, DateTimeOffset.UnixEpoch);
        var sub = Assert.Single(result.PerSubResults);

        Assert.True(sub.IsComplete);
        Assert.Equal(4, sub.VoyageCount);
        Assert.Equal(2, sub.VoyagePreview.Count);
        Assert.Equal(TimeSpan.FromDays(4), sub.Remaining);
        Assert.Equal(TimeSpan.FromDays(4), result.FcCompletionAtUtc - result.GeneratedAtUtc);
    }

    [Fact]
    public void RepoJsonContainsExpectedDownloadLinks()
    {
        var repoJsonPath = FindRepoJson();
        var repoJson = File.ReadAllText(repoJsonPath);

        Assert.Contains("SubmarineEtaPlanner", repoJson);
        Assert.Contains("\"AssemblyVersion\": \"0.2.7.0\"", repoJson);
        Assert.Contains("https://github.com/AlexValliere/submarineEtaPlanner", repoJson);
        Assert.Contains("https://alexvalliere.github.io/submarineEtaPlanner/SubmarineEtaPlanner/latest.zip", repoJson);
        Assert.Contains("\"DalamudApiLevel\": 15", repoJson);
    }

    private static EtaSimulator CreateSimulator(ISubmarineCatalog? catalog = null)
    {
        catalog ??= new CompatSubmarineCatalog();
        var buildResolver = new BuildResolver(catalog);
        var unlockGraph = new RouteUnlockGraph(catalog);
        var selector = new RouteSelector(catalog, unlockGraph);
        return new EtaSimulator(buildResolver, unlockGraph, selector, catalog);
    }

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
            this.unlockRules = unlockRules ?? [new UnlockRule(1, 2, 1)];
            this.routeExp = routeExp;
            this.routeDuration = routeDuration ?? TimeSpan.FromHours(1);
        }

        public IReadOnlyList<UnlockRule> UnlockRules => this.unlockRules;

        public IReadOnlyList<uint> LastMustInclude { get; private set; } = [];

        public int PartBuildResolutionCount { get; private set; }

        public SubmarineBuild ResolveBuild(string buildCode, int rank)
            => new($"R{rank}", rank, 100, 100, 100, 999, 100);

        public SubmarineBuild? ResolveBuild(SubmarineBuildParts buildParts, int rank)
        {
            PartBuildResolutionCount++;
            return buildParts == SubmarineBuildParts.Empty ? null : new SubmarineBuild($"P{rank}", rank, 100, 100, 100, 999, 100);
        }

        public IReadOnlyList<RouteCandidate> GetCandidateRoutes(
            SubmarineBuild build,
            IReadOnlySet<uint> unlockedPoints,
            IReadOnlySet<uint> exploredPoints,
            IReadOnlySet<uint> mustInclude,
            EtaSettings settings,
            DateTimeOffset? deadlineUtc = null)
        {
            LastMustInclude = mustInclude.Order().ToArray();
            var route = LastMustInclude.Count > 0 ? [LastMustInclude[0]] : new uint[] { 99 };
            var durationLimitHours = settings.EffectiveDurationLimitHours;
            if (durationLimitHours > 0 && this.routeDuration > TimeSpan.FromHours(durationLimitHours))
                return [];

            var unlockTargets = this.unlockRules
                .Where(rule => route.Contains(rule.SourcePoint))
                .Where(rule => rule.RequiredRank <= build.Rank)
                .Select(rule => rule.UnlocksPoint)
                .ToArray();

            return [new RouteCandidate(
                route,
                this.routeExp,
                this.routeDuration,
                this.routeExp / Math.Max(this.routeDuration.TotalHours, 0.01),
                unlockTargets,
                settings.EtaModel,
                durationLimitHours > 0)];
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

        public bool IsPostTargetFarmingReady(SubmarineBuild build, IReadOnlySet<uint> unlockedPoints) => false;
    }

    private sealed class MultiRouteCatalog(IReadOnlyList<RouteCandidate> routes) : ISubmarineCatalog
    {
        public IReadOnlyList<UnlockRule> UnlockRules => [];

        public SubmarineBuild ResolveBuild(string buildCode, int rank)
            => new(buildCode, rank, 100, 100, 100, 999, 100);

        public SubmarineBuild? ResolveBuild(SubmarineBuildParts buildParts, int rank)
            => ResolveBuild("TEST", rank);

        public IReadOnlyList<RouteCandidate> GetCandidateRoutes(
            SubmarineBuild build,
            IReadOnlySet<uint> unlockedPoints,
            IReadOnlySet<uint> exploredPoints,
            IReadOnlySet<uint> mustInclude,
            EtaSettings settings,
            DateTimeOffset? deadlineUtc = null)
            => routes
                .Where(route => mustInclude.Count == 0 || route.Route.Any(mustInclude.Contains))
                .Where(route => settings.EffectiveDurationLimitHours <= 0 || route.Duration <= TimeSpan.FromHours(settings.EffectiveDurationLimitHours))
                .OrderByDescending(route => settings.EffectiveOptimizeExpPerHour ? route.ExpPerHour : route.Exp)
                .ThenBy(route => route.Duration)
                .ToArray();

        public uint CalculateExp(IReadOnlyList<uint> route, SubmarineBuild build, ExpMode expMode) => 0;

        public TimeSpan CalculateDuration(IReadOnlyList<uint> route, SubmarineBuild build) => TimeSpan.Zero;

        public (int Rank, uint CurrentExp, uint NextLevelExp) ApplyExp(int rank, uint currentExp, uint gainedExp, int targetRank)
            => (rank, currentExp, 100);

        public string PointName(uint point) => point.ToString();

        public bool IsPostTargetFarmingReady(SubmarineBuild build, IReadOnlySet<uint> unlockedPoints) => false;
    }
}
