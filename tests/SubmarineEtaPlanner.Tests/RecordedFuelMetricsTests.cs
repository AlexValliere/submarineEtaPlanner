using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class RecordedFuelMetricsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(1_000);
    private static readonly ISubmarineCatalog SubmarineCatalog = new StubSubmarineCatalog();

    [Fact]
    public void KnownRouteCalculatesDeterministicSignatureAndFuelWithoutDuration()
    {
        var durationRequested = false;
        var catalog = CreateFuelCatalog(
            new Dictionary<uint, int>
            {
                [10] = 2,
                [13] = 3,
                [15] = 5,
                [18] = 7,
                [26] = 11,
            },
            () => durationRequested = true);
        var observation = Observation(1, 1, 1_000, 26, 10, 18, 13, 15);

        var metrics = RecordedVoyageMetricsCalculator.Calculate(observation, catalog);

        Assert.Equal(new SectorSetSignature("10-13-15-18-26"), metrics.SectorSignature);
        Assert.Equal(28, metrics.CeruleumTanks);
        Assert.True(metrics.FuelKnown);
        Assert.False(durationRequested);
    }

    [Fact]
    public void UnknownSectorKeepsFuelUnavailableInsteadOfUsingPartialTotal()
    {
        var metrics = RecordedVoyageMetricsCalculator.Calculate(
            Observation(1, 1, 1_000, 10, 99),
            CreateFuelCatalog(new Dictionary<uint, int> { [10] = 5 }));

        Assert.Equal(new SectorSetSignature("10-99"), metrics.SectorSignature);
        Assert.Null(metrics.CeruleumTanks);
        Assert.False(metrics.FuelKnown);
    }

    [Fact]
    public void DuplicateSectorContributesFuelOnce()
    {
        var metrics = RecordedVoyageMetricsCalculator.Calculate(
            Observation(1, 1, 1_000, 13, 10, 13, 10),
            CreateFuelCatalog(new Dictionary<uint, int>
            {
                [10] = 5,
                [13] = 8,
            }));

        Assert.Equal(new SectorSetSignature("10-13"), metrics.SectorSignature);
        Assert.Equal(13, metrics.CeruleumTanks);
        Assert.True(metrics.FuelKnown);
    }

    [Fact]
    public void ZeroGilKnownFuelVoyageAddsTanksAndLowersGilPerTank()
    {
        var fc = CreateFc(
            (1, [
                Observation(1, 2, 1_000, 10),
                Observation(1, 1, 0, 10),
            ]));

        var metrics = IncomeMetricsCalculator.Calculate(
            fc,
            Now,
            period: null,
            SubmarineCatalog,
            CreateFuelCatalog(new Dictionary<uint, int> { [10] = 10 }));
        var submarine = Assert.Single(metrics.Submarines);

        Assert.Equal(2, submarine.KnownFuelVoyageCount);
        Assert.Equal(0, submarine.UnknownFuelVoyageCount);
        Assert.Equal(20, submarine.TotalRecordedTanks);
        Assert.Equal(10, submarine.AverageTanksPerVoyage);
        Assert.Equal(50, submarine.GrossGilPerTank);
    }

    [Fact]
    public void MixedKnownAndUnknownHistoryExcludesUnknownVoyageFromEfficiencyOnly()
    {
        var knownSignature = SectorSetSignature.Create([10]);
        var unknownSignature = SectorSetSignature.Create([99]);
        var fc = CreateFc(
            (1, [
                Observation(1, 2, 1_000, 10),
                Observation(1, 1, 9_000, 99),
            ]));

        var metrics = IncomeMetricsCalculator.Calculate(
            fc,
            Now,
            period: null,
            SubmarineCatalog,
            CreateFuelCatalog(new Dictionary<uint, int> { [10] = 5 }));
        var submarine = Assert.Single(metrics.Submarines);

        Assert.Equal(10_000, submarine.GrossGil);
        Assert.Equal(1, submarine.KnownFuelVoyageCount);
        Assert.Equal(1, submarine.UnknownFuelVoyageCount);
        Assert.Equal(5, submarine.TotalRecordedTanks);
        Assert.Equal(5, submarine.AverageTanksPerVoyage);
        Assert.Equal(200, submarine.GrossGilPerTank);
        Assert.Equal(1_000, submarine.GrossGilByRouteSignature[knownSignature]);
        Assert.Equal(9_000, submarine.GrossGilByRouteSignature[unknownSignature]);
    }

    [Fact]
    public void RouteSignatureEqualityIgnoresHistoricalSectorRowOrderAndDuplicates()
    {
        var first = SectorSetSignature.Create([26, 10, 15, 13, 18, 10]);
        var second = SectorSetSignature.Create([18, 26, 13, 15, 10]);

        Assert.Equal(new SectorSetSignature("10-13-15-18-26"), first);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void FcAggregationCombinesSubmarineFuelAndRouteGrossGil()
    {
        var routeTen = SectorSetSignature.Create([10]);
        var routeThirteen = SectorSetSignature.Create([13]);
        var unknownRoute = SectorSetSignature.Create([99]);
        var fc = CreateFc(
            (1, [
                Observation(1, 3, 100, 10),
                Observation(1, 2, 900, 99),
            ]),
            (2, [Observation(2, 1, 300, 13)]));

        var metrics = IncomeMetricsCalculator.Calculate(
            fc,
            Now,
            period: null,
            SubmarineCatalog,
            CreateFuelCatalog(new Dictionary<uint, int>
            {
                [10] = 5,
                [13] = 15,
            }));

        Assert.Equal(2, metrics.KnownFuelVoyageCount);
        Assert.Equal(1, metrics.UnknownFuelVoyageCount);
        Assert.Equal(20, metrics.TotalRecordedTanks);
        Assert.Equal(10, metrics.AverageTanksPerVoyage);
        Assert.Equal(20, metrics.GrossGilPerTank);
        Assert.Equal(100, metrics.GrossGilByRouteSignature[routeTen]);
        Assert.Equal(300, metrics.GrossGilByRouteSignature[routeThirteen]);
        Assert.Equal(900, metrics.GrossGilByRouteSignature[unknownRoute]);
        Assert.Equal([1, 1], metrics.Submarines.Select(submarine => submarine.KnownFuelVoyageCount));
        Assert.Equal([1, 0], metrics.Submarines.Select(submarine => submarine.UnknownFuelVoyageCount));
    }

    private static RouteOperationalCalculator CreateFuelCatalog(
        IReadOnlyDictionary<uint, int> tankRequirementBySector,
        Action? onDurationRequested = null)
        => new(
            tankRequirementBySector,
            (_, _) =>
            {
                onDurationRequested?.Invoke();
                return TimeSpan.FromHours(1);
            });

    private static VoyageObservation Observation(
        long submarineId,
        int daysAgo,
        long grossGil,
        params uint[] sectorIds)
        => new(
            "01",
            1,
            submarineId,
            Now.AddDays(-daysAgo),
            sectorIds,
            100,
            0,
            0,
            0,
            grossGil == 0 ? [] : [new SalvageItemTotal(1, "Salvage", 1, grossGil)]);

    private static FcState CreateFc(
        params (long SubmarineId, VoyageObservation[] Observations)[] histories)
    {
        byte[] fcId = [1];
        var submarines = histories.Select(history =>
        {
            var ordered = history.Observations.OrderBy(observation => observation.ReturnAtUtc).ToArray();
            var salvageVoyages = ordered.Select(observation => new SalvageVoyageRecord(
                observation.FcIdKey,
                observation.SubmarineId,
                observation.ReturnAtUtc,
                observation.Items)).ToArray();
            return new SubmarineState(
                fcId,
                history.SubmarineId,
                $"Sub {history.SubmarineId}",
                100,
                0,
                1,
                SubmarineBuildParts.Empty,
                DateTimeOffset.MinValue,
                [],
                true,
                [])
            {
                VoyageHistory = ordered,
                Salvage = new SubmarineSalvageSummary(
                    ordered.Length,
                    ordered.FirstOrDefault()?.ReturnAtUtc,
                    ordered.LastOrDefault()?.ReturnAtUtc,
                    ordered.SelectMany(observation => observation.Items).ToArray())
                {
                    Voyages = salvageVoyages,
                },
            };
        }).ToArray();

        return new FcState(fcId, "FC", "World", new HashSet<uint>(), new HashSet<uint>(), submarines);
    }

    private sealed class StubSubmarineCatalog : ISubmarineCatalog
    {
        public int MaximumRank => 100;
        public IReadOnlyList<UnlockRule> UnlockRules => [];
        public SubmarineBuild ResolveBuild(string buildCode, int rank) => new(buildCode, rank, 0, 0, 0, 0, 0);
        public SubmarineBuild? ResolveBuild(SubmarineBuildParts buildParts, int rank) => null;
        public RouteSearchResult FindBestRoute(RouteSearchRequest request) => new(null, 0, false);
        public uint CalculateExp(IReadOnlyList<uint> route, SubmarineBuild build, ExpMode expMode) => 0;
        public TimeSpan CalculateDuration(IReadOnlyList<uint> route, SubmarineBuild build) => TimeSpan.Zero;
        public (int Rank, uint CurrentExp, uint NextLevelExp) ApplyExp(int rank, uint currentExp, uint gainedExp, int targetRank)
            => (rank, currentExp, 0);
        public string PointName(uint point) => point.ToString();
        public int GetPointRequiredRank(uint point) => 1;
    }
}
