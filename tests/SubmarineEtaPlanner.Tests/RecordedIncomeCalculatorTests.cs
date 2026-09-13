using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class RecordedIncomeCalculatorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(1_000);

    public static IEnumerable<object?[]> NewFarmingCases()
    {
        foreach (int? periodDays in new int?[] { 7, 30, 90, 365, null })
        foreach (var (minutes, expectedDays) in new (int, double)[]
                 { (0, 1), (30, 1), (60, 1), (1439, 1), (1440, 1), (2160, 1.5), (2880, 2) })
            yield return [periodDays, minutes, expectedDays];
    }

    [Theory]
    [MemberData(nameof(NewFarmingCases))]
    public void NewFarmingReturnsUseAtLeastOneDayAtEveryLevel(
        int? periodDays, int elapsedMinutes, double expectedDays)
    {
        var fc = CreateFc(
            1,
            CreateSubmarine(1, "Sous-marin -1", (10, 0), (0, 332_500), (-7, 99_999)),
            CreateSubmarine(2, "Sous-marin -2", (0, 255_000)),
            CreateSubmarine(3, "Sous-marin -3", (10, 0)),
            CreateSubmarine(4, "Sous-marin -4"));
        var now = Now.AddMinutes(elapsedMinutes);
        var period = periodDays is { } days ? TimeSpan.FromDays(days) : (TimeSpan?)null;

        var metrics = IncomeMetricsCalculator.Calculate(fc, now, period);
        var summary = IncomeMetricsCalculator.Summarize([metrics], now, period);

        Assert.Equal(587_500, metrics.GrossGil);
        Assert.Equal(2, metrics.VoyageCount);
        Assert.Equal(293_750, metrics.GilPerVoyage);
        Assert.Equal(expectedDays, metrics.CoveredDays);
        Assert.Equal(587_500 / expectedDays, metrics.RecordedAverageGilPerDay);
        Assert.Equal(Now, metrics.FirstReturnAtUtc);
        Assert.Equal(Now, metrics.LastReturnAtUtc);
        Assert.Equal(metrics.GrossGil, summary.GrossGil);
        Assert.Equal(metrics.VoyageCount, summary.VoyageCount);
        Assert.Equal(metrics.GilPerVoyage, summary.GilPerVoyage);
        Assert.Equal(expectedDays, summary.CoveredDays);
        Assert.Equal(metrics.RecordedAverageGilPerDay, summary.RecordedAverageGilPerDay);

        foreach (var (submarine, expectedGil) in metrics.Submarines.Take(2).Zip(new long[] { 332_500, 255_000 }))
        {
            Assert.Equal(expectedGil, submarine.GrossGil);
            Assert.Equal(1, submarine.VoyageCount);
            Assert.Equal((double)expectedGil, submarine.GilPerVoyage);
            Assert.Equal(expectedDays, submarine.CoveredDays);
            Assert.Equal(expectedGil / expectedDays, submarine.RecordedAverageGilPerDay);
            Assert.Equal(Now, submarine.FirstReturnAtUtc);
            Assert.Equal(Now, submarine.LastReturnAtUtc);
        }

        Assert.All(metrics.Submarines.Skip(2), submarine =>
        {
            Assert.Equal(0, submarine.GrossGil);
            Assert.Equal(0, submarine.VoyageCount);
            Assert.Equal(0, submarine.CoveredDays);
            Assert.Equal(0, submarine.RecordedAverageGilPerDay);
            Assert.Null(submarine.FirstReturnAtUtc);
            Assert.Null(submarine.LastReturnAtUtc);
        });
    }

    [Fact]
    public void NewAndEstablishedFleetsKeepSharedCoverageAndSortByCorrectedAverage()
    {
        var established = IncomeMetricsCalculator.Calculate(
            CreateFc(1,
                CreateSubmarine(1, "Established", (2, 2_400_000)),
                CreateSubmarine(2, "New", (0, 332_500))),
            Now, period: null);
        var newcomer = IncomeMetricsCalculator.Calculate(
            CreateFc(2, CreateSubmarine(3, "New FC", (0, 255_000))),
            Now, period: null);

        var summary = IncomeMetricsCalculator.Summarize([established, newcomer], Now, period: null);
        var ordered = IncomeMetricsOrdering.Order(
            [newcomer, established], IncomeSort.RecordedAverageGilPerDay, _ => false);

        Assert.Equal(2, established.CoveredDays);
        Assert.Equal(1_366_250, established.RecordedAverageGilPerDay);
        Assert.Equal(332_500, established.Submarines[1].RecordedAverageGilPerDay);
        Assert.Equal(255_000, newcomer.RecordedAverageGilPerDay);
        Assert.Equal(2, summary.CoveredDays);
        Assert.Equal(2_987_500, summary.GrossGil);
        Assert.Equal(1_493_750, summary.RecordedAverageGilPerDay);
        Assert.Equal([established.FcIdKey, newcomer.FcIdKey], ordered.Select(metric => metric.FcIdKey));
    }

    [Fact]
    public void StaggeredSubmarinesAndMultipleFcsUseOneSharedRecordedAverage()
    {
        var firstFc = CreateFc(
            1,
            CreateSubmarine(1, "Established", (100, 10_000)),
            CreateSubmarine(2, "New", (10, 10_000)));
        var secondFc = CreateFc(
            2,
            CreateSubmarine(3, "Other FC", (20, 10_000)));

        var firstMetrics = IncomeMetricsCalculator.Calculate(firstFc, Now, period: null);
        var secondMetrics = IncomeMetricsCalculator.Calculate(secondFc, Now, period: null);
        var summary = IncomeMetricsCalculator.Summarize([firstMetrics, secondMetrics], Now, period: null);

        Assert.Equal(20_000, firstMetrics.GrossGil);
        Assert.Equal(2, firstMetrics.VoyageCount);
        Assert.Equal(10_000, firstMetrics.GilPerVoyage);
        Assert.Equal(100, firstMetrics.CoveredDays);
        Assert.Equal(200, firstMetrics.RecordedAverageGilPerDay);
        Assert.Equal(firstMetrics.RecordedAverageGilPerDay, firstMetrics.GilPerDay);

        Assert.Equal(30_000, summary.GrossGil);
        Assert.Equal(3, summary.VoyageCount);
        Assert.Equal(10_000, summary.GilPerVoyage);
        Assert.Equal(100, summary.CoveredDays);
        Assert.Equal(300, summary.RecordedAverageGilPerDay);
        Assert.Equal(summary.RecordedAverageGilPerDay, summary.GilPerDay);
    }

    [Fact]
    public void VoyagesWithoutTrackedSalvageDoNotCountTowardIncomeMetrics()
    {
        var fc = CreateFc(
            1,
            CreateSubmarine(1, "Zero included", (10, 0), (1, 1_000)));

        var metrics = IncomeMetricsCalculator.Calculate(fc, Now, TimeSpan.FromDays(30));
        var submarine = Assert.Single(metrics.Submarines);

        Assert.Equal(1_000, submarine.GrossGil);
        Assert.Equal(1, submarine.VoyageCount);
        Assert.Equal(1_000, submarine.GilPerVoyage);
        Assert.Equal(1, submarine.CoveredDays);
        Assert.Equal(1_000, submarine.RecordedAverageGilPerDay);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(365)]
    public void SelectedPeriodsIncludeBoundaryAndExcludeOlderAndFutureRows(int periodDays)
    {
        var fc = CreateFc(
            1,
            CreateSubmarine(
                1,
                "Windowed",
                (periodDays + 1, 99_999),
                (periodDays, periodDays * 100L),
                (1, 0),
                (-1, 88_888)));

        var metrics = IncomeMetricsCalculator.Calculate(fc, Now, TimeSpan.FromDays(periodDays));

        Assert.Equal(periodDays * 100L, metrics.GrossGil);
        Assert.Equal(1, metrics.VoyageCount);
        Assert.Equal(periodDays * 100d, metrics.GilPerVoyage);
        Assert.Equal(periodDays, metrics.CoveredDays);
        Assert.Equal(100, metrics.RecordedAverageGilPerDay);
        Assert.Equal(Now.AddDays(-periodDays), metrics.FirstReturnAtUtc);
        Assert.Equal(Now.AddDays(-periodDays), metrics.LastReturnAtUtc);
    }

    [Fact]
    public void LifetimeUsesFirstObservationAndExcludesFutureRows()
    {
        var fc = CreateFc(
            1,
            CreateSubmarine(1, "Lifetime", (400, 40_000), (1, 0), (-1, 50_000)));

        var metrics = IncomeMetricsCalculator.Calculate(fc, Now, period: null);

        Assert.Equal(40_000, metrics.GrossGil);
        Assert.Equal(1, metrics.VoyageCount);
        Assert.Equal(40_000, metrics.GilPerVoyage);
        Assert.Equal(400, metrics.CoveredDays);
        Assert.Equal(100, metrics.RecordedAverageGilPerDay);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(365)]
    [InlineData(null)]
    public void NoQualifyingObservationsProducesNoCoverageOrIncome(int? periodDays)
    {
        var period = periodDays is { } days ? TimeSpan.FromDays(days) : (TimeSpan?)null;
        var metrics = IncomeMetricsCalculator.Calculate(
            CreateFc(1,
                CreateSubmarine(1, "Empty"),
                CreateSubmarine(2, "No salvage", (1, 0)),
                CreateSubmarine(3, "Future only", (-1, 500_000))),
            Now,
            period);
        var summary = IncomeMetricsCalculator.Summarize([metrics], Now, period);

        Assert.Equal(0, metrics.GrossGil);
        Assert.Equal(0, metrics.VoyageCount);
        Assert.Equal(0, metrics.GilPerVoyage);
        Assert.Equal(0, metrics.CoveredDays);
        Assert.Equal(0, metrics.RecordedAverageGilPerDay);
        Assert.Null(metrics.FirstReturnAtUtc);
        Assert.Null(metrics.LastReturnAtUtc);
        Assert.Equal(0, summary.CoveredDays);
        Assert.Equal(0, summary.RecordedAverageGilPerDay);
    }

    [Fact]
    public void RecordedAverageSortReusesLegacyDailySortValues()
    {
        Assert.Equal(1, (int)IncomeSort.RecordedAverageGilPerDay);
        Assert.Equal(IncomeSort.RecordedAverageGilPerDay, IncomeSortPreferences.Normalize((IncomeSort)4));

        var higherRecordedAverage = Metrics("higher", recordedAverage: 500);
        var lowerRecordedAverage = Metrics("lower", recordedAverage: 400);

        var byRecorded = IncomeMetricsOrdering.Order(
            [lowerRecordedAverage, higherRecordedAverage],
            IncomeSort.RecordedAverageGilPerDay,
            _ => false);
        var byLegacyRecorded = IncomeMetricsOrdering.Order(
            [lowerRecordedAverage, higherRecordedAverage],
            (IncomeSort)4,
            _ => false);

        Assert.Equal(["higher", "lower"], byRecorded.Select(metric => metric.FcIdKey));
        Assert.Equal(["higher", "lower"], byLegacyRecorded.Select(metric => metric.FcIdKey));
    }

    [Fact]
    public void InvalidIncomeSortNormalizesToGrossGil()
    {
        Assert.Equal(IncomeSort.GrossGil, IncomeSortPreferences.Normalize((IncomeSort)999));
    }

    private static IncomeFcMetrics Metrics(string id, double recordedAverage)
        => new(
            id,
            id,
            1_000,
            1,
            1_000,
            10,
            recordedAverage,
            Now.AddDays(-10),
            Now,
            []);

    private static FcState CreateFc(byte fcId, params SubmarineState[] submarines)
        => new(
            [fcId],
            $"FC {fcId}",
            "World",
            new HashSet<uint>(),
            new HashSet<uint>(),
            submarines.Select(submarine => submarine with { FcId = [fcId] }).ToArray());

    private static SubmarineState CreateSubmarine(
        long submarineId,
        string name,
        params (int DaysAgo, long Gil)[] voyages)
    {
        byte[] fcId = [0];
        var records = voyages
            .Select(voyage => new SalvageVoyageRecord(
                Convert.ToHexString(fcId),
                submarineId,
                Now.AddDays(-voyage.DaysAgo),
                voyage.Gil == 0
                    ? []
                    : [new SalvageItemTotal(1, "Salvage", 1, voyage.Gil)]))
            .ToArray();
        return new SubmarineState(
            fcId,
            submarineId,
            name,
            100,
            0,
            1,
            SubmarineBuildParts.Empty,
            DateTimeOffset.MinValue,
            [],
            true,
            [])
        {
            Salvage = new SubmarineSalvageSummary(
                records.Length,
                records.Select(record => (DateTimeOffset?)record.ReturnAtUtc).Min(),
                records.Select(record => (DateTimeOffset?)record.ReturnAtUtc).Max(),
                [])
            {
                Voyages = records,
            },
        };
    }
}
