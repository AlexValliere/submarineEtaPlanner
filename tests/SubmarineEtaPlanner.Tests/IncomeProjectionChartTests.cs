using SubmarineEtaPlanner.Planner;
using Xunit;
using static SubmarineEtaPlanner.Tests.IncomeProjectionTestData;

namespace SubmarineEtaPlanner.Tests;

public sealed class IncomeProjectionChartTests
{
    [Theory]
    [InlineData(30, "2026-09-21T12:13:47Z")]
    [InlineData(90, "2026-09-21T12:13:47Z")]
    [InlineData(365, "2026-09-21T12:13:47Z")]
    [InlineData(365, "2028-01-31T23:11:00Z")]
    public void BucketsPreserveExactSummaryTotalsAndCoverEntireHorizon(int days, string start)
    {
        var now = DateTimeOffset.Parse(start);
        var totals = new IncomeProjectionTotals(123_456.789012345678901234m, 2, 3);
        var horizon = (IncomeProjectionHorizon)days;
        var series = IncomeProjectionChartBuilder.Build(totals, horizon, now, TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"));
        Assert.Equal(totals.ProjectedGil(horizon), series.EstimatedGil);
        Assert.Equal(now, series.Buckets[0].StartAtUtc);
        Assert.Equal(now.AddDays(days), series.Buckets[^1].EndAtUtc);
        Assert.Equal(days == 365, series.Monthly);
        Assert.True(series.Buckets[0].IsPartial);
        Assert.True(series.Buckets[^1].IsPartial);
        Assert.All(series.Buckets, bucket => Assert.True(bucket.EstimatedGil > 0));
        for (var i = 1; i < series.Buckets.Count; i++) Assert.Equal(series.Buckets[i - 1].EndAtUtc, series.Buckets[i].StartAtUtc);
    }

    [Fact]
    public void MonthlyBarsUseCalendarMonthLengthsAndIncludeLeapDay()
    {
        var now = new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var series = IncomeProjectionChartBuilder.Build(new(100m, 1, 1), IncomeProjectionHorizon.Days365, now, TimeZoneInfo.Utc);
        Assert.Equal(12, series.Buckets.Count);
        Assert.Equal(3100m, series.Buckets[0].EstimatedGil);
        Assert.Equal(2900m, series.Buckets[1].EstimatedGil);
        Assert.Equal(new DateOnly(2028, 2, 29), series.Buckets[1].EndDate);
        Assert.Equal(36_500m, series.EstimatedGil);
        Assert.False(series.Buckets[0].IsPartial);
        Assert.True(series.Buckets[^1].IsPartial);
    }

    [Theory]
    [InlineData(3, 29, 23)]
    [InlineData(10, 25, 25)]
    public void DailyBarsProrateElapsedTimeAcrossDst(int month, int day, int hours)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
        var transition = new DateOnly(2026, month, day);
        var now = IncomeChartSeriesBuilder.StartOfDayUtc(transition.AddDays(-1), zone);
        var series = IncomeProjectionChartBuilder.Build(new(2400m, 1, 1), IncomeProjectionHorizon.Days30, now, zone);
        var bucket = Assert.Single(series.Buckets, bucket => bucket.StartDate == transition);
        Assert.Equal(TimeSpan.FromHours(hours), bucket.EndAtUtc - bucket.StartAtUtc);
        Assert.Equal(hours * 100m, bucket.EstimatedGil);
        Assert.Equal(72_000m, series.EstimatedGil);
    }

    [Fact]
    public void SupportedZeroHasBucketsWhileUnavailableHasNoneAndAxesRemainFinite()
    {
        var zero = IncomeProjectionChartBuilder.Build(new(0m, 1, 1), IncomeProjectionHorizon.Days30, Now, TimeZoneInfo.Utc);
        var unknown = IncomeProjectionChartBuilder.Build(new(null, 0, 1), IncomeProjectionHorizon.Days30, Now, TimeZoneInfo.Utc);
        Assert.NotEmpty(zero.Buckets);
        Assert.Equal(0m, zero.EstimatedGil);
        Assert.Equal(1d, zero.AxisMaximum);
        Assert.Empty(unknown.Buckets);
        Assert.Null(unknown.EstimatedGil);
        var large = IncomeProjectionChartBuilder.Build(new(9_000_000_000_000_000m, 1, 1), IncomeProjectionHorizon.Days365, Now, TimeZoneInfo.Utc);
        Assert.True(double.IsFinite(large.AxisMaximum));
        Assert.Equal(3_285_000_000_000_000_000m, large.EstimatedGil);
    }

    [Fact]
    public void CacheSkipsCollapsedChartAndInvalidatesForInputsTimeZoneMidnightAndRollback()
    {
        var cache = new IncomeProjectionChartCache();
        var totals = new IncomeProjectionTotals(100m, 1, 1);
        var now = new DateTimeOffset(2026, 9, 21, 23, 59, 45, TimeSpan.Zero);
        Assert.Null(cache.Get(false, totals, IncomeProjectionHorizon.Days365, now, TimeZoneInfo.Utc));
        Assert.Equal(0, cache.BuildCount);
        var first = cache.Get(true, totals, IncomeProjectionHorizon.Days365, now, TimeZoneInfo.Utc);
        Assert.Same(first, cache.Get(true, totals, IncomeProjectionHorizon.Days365, now.AddSeconds(10), TimeZoneInfo.Utc));
        var midnight = cache.Get(true, totals, IncomeProjectionHorizon.Days365, now.AddSeconds(15), TimeZoneInfo.Utc);
        Assert.NotSame(first, midnight);
        var rollback = cache.Get(true, totals, IncomeProjectionHorizon.Days365, now, TimeZoneInfo.Utc);
        Assert.NotSame(midnight, rollback);
        var changedZone = cache.Get(true, totals, IncomeProjectionHorizon.Days365, now, TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"));
        Assert.NotSame(rollback, changedZone);
        var horizon = cache.Get(true, totals, IncomeProjectionHorizon.Days30, now, TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"));
        Assert.NotSame(changedZone, horizon);
        var partial = cache.Get(true, totals with { FarmingSubmarines = 2 }, IncomeProjectionHorizon.Days30, now, TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"));
        Assert.NotSame(horizon, partial);
        var changedRate = cache.Get(true, totals with { GilPerDay = 200 }, IncomeProjectionHorizon.Days30, now, TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"));
        Assert.NotSame(partial, changedRate);
        Assert.Equal(7, cache.BuildCount);
    }
}
