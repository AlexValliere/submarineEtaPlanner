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
        var settings = EtaSettings.CreateDefault() with { SimulationMode = SimulationMode.Fleet };
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
        var settings = EtaSettings.CreateDefault() with { SimulationMode = SimulationMode.Fleet };
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
    public void RepoJsonContainsExpectedDownloadLinks()
    {
        var repoJsonPath = FindRepoJson();
        var repoJson = File.ReadAllText(repoJsonPath);

        Assert.Contains("SubmarineEtaPlanner", repoJson);
        Assert.Contains("https://github.com/AlexValliere/submarineEtaPlanner", repoJson);
        Assert.Contains("https://alexvalliere.github.io/submarineEtaPlanner/SubmarineEtaPlanner/latest.zip", repoJson);
        Assert.Contains("\"DalamudApiLevel\": 15", repoJson);
    }

    private static EtaSimulator CreateSimulator()
    {
        var catalog = new CompatSubmarineCatalog();
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
}
