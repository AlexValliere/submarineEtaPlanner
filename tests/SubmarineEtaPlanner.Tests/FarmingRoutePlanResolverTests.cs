using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class FarmingRoutePlanResolverTests
{
    private const int TargetRank = 90;

    [Fact]
    public void PinnedRouteOverridesCurrentTrackerRoute()
    {
        var submarine = CreateSubmarine(1, rank: 90, currentRoute: [1, 2]);
        var plan = Assert.Single(Resolve(
            [submarine],
            new Dictionary<long, SubmarinePreferences>
            {
                [1] = new()
                {
                    Assignment = SubmarineAssignment.Farming,
                    PinnedFarmingRoute = [3, 4],
                },
            }));

        Assert.Equal(FarmingRouteSource.Pinned, plan.Source);
        Assert.Equal([3u, 4u], plan.Route);
        Assert.Equal(17, plan.Fuel.CeruleumTanks);
        Assert.Equal(TimeSpan.FromHours(12), plan.VoyageDuration);
        Assert.Empty(plan.Warnings);
        Assert.True(plan.IsUsable);
    }

    [Fact]
    public void CurrentTrackerRouteIsUsedWithoutPin()
    {
        var plan = Assert.Single(Resolve(
            [CreateSubmarine(1, rank: 90, currentRoute: [4, 2, 1])],
            new Dictionary<long, SubmarinePreferences>
            {
                [1] = new() { Assignment = SubmarineAssignment.Farming },
            }));

        Assert.Equal(FarmingRouteSource.CurrentTrackerRoute, plan.Source);
        Assert.Equal([4u, 2u, 1u], plan.Route);
        Assert.True(plan.IsUsable);
    }

    [Fact]
    public void MissingRouteReturnsSpecificWarningsWithoutZeroSubstitution()
    {
        var plan = Assert.Single(Resolve(
            [CreateSubmarine(1, rank: 90)],
            new Dictionary<long, SubmarinePreferences>
            {
                [1] = new() { Assignment = SubmarineAssignment.Farming },
            }));

        Assert.Equal(FarmingRouteSource.Missing, plan.Source);
        Assert.Empty(plan.Route);
        Assert.Null(plan.Fuel.CeruleumTanks);
        Assert.False(plan.Fuel.IsComplete);
        Assert.Null(plan.VoyageDuration);
        Assert.Equal(
            [
                "Farming route is empty.",
                "Voyage duration is zero or unavailable.",
                "Fuel calculation is incomplete.",
            ],
            plan.Warnings);
        Assert.False(plan.IsUsable);
    }

    [Fact]
    public void UnknownSectorMakesFuelIncomplete()
    {
        var plan = Assert.Single(Resolve(
            [CreateSubmarine(1, rank: 90, currentRoute: [2, 99])],
            new Dictionary<long, SubmarinePreferences>
            {
                [1] = new() { Assignment = SubmarineAssignment.Farming },
            }));

        Assert.Equal(8, plan.Fuel.CeruleumTanks);
        Assert.False(plan.Fuel.IsComplete);
        Assert.Equal([99u], plan.Fuel.UnknownSectors);
        Assert.Equal(
            [
                "Route contains unknown sectors: 99.",
                "Fuel calculation is incomplete.",
            ],
            plan.Warnings);
        Assert.False(plan.IsUsable);
    }

    [Fact]
    public void MissingBuildLeavesDurationUnavailableButStillCalculatesFuel()
    {
        var plan = Assert.Single(Resolve(
            [CreateSubmarine(1, rank: 90, currentRoute: [1]) with { BuildParts = SubmarineBuildParts.Empty }],
            new Dictionary<long, SubmarinePreferences>
            {
                [1] = new() { Assignment = SubmarineAssignment.Farming },
            }));

        Assert.False(plan.Build.IsAvailable);
        Assert.Equal(5, plan.Fuel.CeruleumTanks);
        Assert.True(plan.Fuel.IsComplete);
        Assert.Null(plan.VoyageDuration);
        Assert.Equal(
            [
                "Current build could not be resolved.",
                "Voyage duration is zero or unavailable.",
            ],
            plan.Warnings);
        Assert.False(plan.IsUsable);
    }

    [Fact]
    public void PausedAndLevelingSubmarinesAreExcluded()
    {
        var plans = Resolve(
            [
                CreateSubmarine(1, "Leveling", rank: 100, currentRoute: [1]),
                CreateSubmarine(2, "Paused", rank: 100, currentRoute: [2]),
                CreateSubmarine(3, "Farming", rank: 50, currentRoute: [3]),
            ],
            new Dictionary<long, SubmarinePreferences>
            {
                [1] = new() { Assignment = SubmarineAssignment.Leveling },
                [2] = new() { Assignment = SubmarineAssignment.Paused },
                [3] = new() { Assignment = SubmarineAssignment.Farming },
            });

        var plan = Assert.Single(plans);
        Assert.Equal(3, plan.SubmarineId);
        Assert.Equal("Farming", plan.SubmarineName);
    }

    [Fact]
    public void HistoricalSectorSetIsNeverSelectedAsOrderedRoute()
    {
        var submarine = CreateSubmarine(1, rank: 90) with
        {
            VoyageHistory =
            [
                new VoyageObservation(
                    "01",
                    1,
                    1,
                    DateTimeOffset.UnixEpoch,
                    [4, 2, 1],
                    90,
                    0,
                    0,
                    0,
                    []),
            ],
        };

        var plan = Assert.Single(Resolve(
            [submarine],
            new Dictionary<long, SubmarinePreferences>
            {
                [1] = new() { Assignment = SubmarineAssignment.Farming },
            }));

        Assert.Equal(FarmingRouteSource.Missing, plan.Source);
        Assert.Empty(plan.Route);
    }

    [Fact]
    public void WarningsAreDeterministic()
    {
        var submarine = CreateSubmarine(1, rank: 90, currentRoute: [9, 7, 9]) with
        {
            BuildParts = SubmarineBuildParts.Empty,
        };

        var plan = Assert.Single(Resolve(
            [submarine],
            new Dictionary<long, SubmarinePreferences>
            {
                [1] = new() { Assignment = SubmarineAssignment.Farming },
            }));

        Assert.Equal([7u, 9u], plan.Fuel.UnknownSectors);
        Assert.Equal(
            [
                "Current build could not be resolved.",
                "Route contains unknown sectors: 7, 9.",
                "Voyage duration is zero or unavailable.",
                "Fuel calculation is incomplete.",
            ],
            plan.Warnings);
    }

    [Fact]
    public void ZeroDurationIsReportedAsUnavailable()
    {
        var plan = Assert.Single(Resolve(
            [CreateSubmarine(1, rank: 90, currentRoute: [1])],
            new Dictionary<long, SubmarinePreferences>
            {
                [1] = new() { Assignment = SubmarineAssignment.Farming },
            },
            duration: TimeSpan.Zero));

        Assert.Null(plan.VoyageDuration);
        Assert.Equal(["Voyage duration is zero or unavailable."], plan.Warnings);
        Assert.False(plan.IsUsable);
    }

    private static IReadOnlyList<FarmingRoutePlan> Resolve(
        IReadOnlyList<SubmarineState> submarines,
        Dictionary<long, SubmarinePreferences> submarinePreferences,
        TimeSpan? duration = null)
    {
        var catalog = new TestCatalog();
        var operationalCatalog = new RouteOperationalCalculator(
            new Dictionary<uint, int>
            {
                [1] = 5,
                [2] = 8,
                [3] = 7,
                [4] = 10,
            },
            (_, _) => duration ?? TimeSpan.FromHours(12));
        var freeCompany = new FcState(
            [1],
            "FC",
            "World",
            new HashSet<uint>(),
            new HashSet<uint>(),
            submarines);
        var preferences = new FcPreferences { Submarines = submarinePreferences };

        return FarmingRoutePlanResolver.Resolve(
            freeCompany,
            preferences,
            TargetRank,
            catalog,
            operationalCatalog);
    }

    private static SubmarineState CreateSubmarine(
        long id,
        string? name = null,
        int rank = 90,
        IReadOnlyList<uint>? currentRoute = null)
        => new(
            [1],
            id,
            name ?? $"Sub {id}",
            rank,
            0,
            1_000,
            new SubmarineBuildParts(1, 2, 3, 4),
            DateTimeOffset.UnixEpoch.AddHours(12),
            currentRoute ?? [],
            CurrentVoyageKnown: currentRoute is { Count: > 0 },
            ManualCurrentRouteOverride: []);

    private sealed class TestCatalog : ISubmarineCatalog
    {
        public int MaximumRank => 120;

        public IReadOnlyList<UnlockRule> UnlockRules => [];

        public SubmarineBuild ResolveBuild(string buildCode, int rank)
            => new(buildCode, rank, 0, 0, 0, 0, 100);

        public SubmarineBuild? ResolveBuild(SubmarineBuildParts buildParts, int rank)
            => buildParts == SubmarineBuildParts.Empty ? null : ResolveBuild("SSUW", rank);

        public RouteSearchResult FindBestRoute(RouteSearchRequest request)
            => new(null, 0, false);

        public uint CalculateExp(IReadOnlyList<uint> route, SubmarineBuild build, ExpMode expMode)
            => 0;

        public TimeSpan CalculateDuration(IReadOnlyList<uint> route, SubmarineBuild build)
            => TimeSpan.Zero;

        public (int Rank, uint CurrentExp, uint NextLevelExp) ApplyExp(
            int rank,
            uint currentExp,
            uint gainedExp,
            int targetRank)
            => (rank, currentExp, 1_000);

        public string PointName(uint point) => point.ToString();

        public int GetPointRequiredRank(uint point) => 1;
    }
}
