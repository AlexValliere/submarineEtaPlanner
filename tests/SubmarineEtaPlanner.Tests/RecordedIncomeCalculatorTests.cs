using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class RecordedIncomeCalculatorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(1_000);

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

    [Fact]
    public void NoObservationsProducesNoCoverageOrIncome()
    {
        var metrics = IncomeMetricsCalculator.Calculate(
            CreateFc(1, CreateSubmarine(1, "Empty")),
            Now,
            TimeSpan.FromDays(30));
        var summary = IncomeMetricsCalculator.Summarize([metrics], Now, TimeSpan.FromDays(30));

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
