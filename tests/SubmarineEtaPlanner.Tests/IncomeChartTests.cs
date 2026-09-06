using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class IncomeChartTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RecordedZeroAndAbsentDaysHaveDifferentStates()
    {
        var fc = Fc(1, (1, Now.AddDays(-2), 1_000), (1, Now.AddDays(-1), 0));
        var series = Build([fc], TimeSpan.FromDays(7));
        Assert.Equal(8, series.Buckets.Count); // The existing rolling window includes two partial boundary dates.
        Assert.Equal(IncomeChartBucketState.RecordedGil, Day(series, new(2026, 9, 4)).State);
        Assert.Equal(IncomeChartBucketState.RecordedZero, Day(series, new(2026, 9, 5)).State);
        Assert.Equal(IncomeChartBucketState.NoRecordedReturns, Day(series, new(2026, 9, 3)).State);
        Assert.Equal(1, Day(series, new(2026, 9, 5)).RecordedReturns);
        Assert.Equal(0, Day(series, new(2026, 9, 3)).RecordedReturns);
        Assert.True(series.Buckets[0].IsPartial);
        Assert.True(series.Buckets[^1].IsPartial);
        Assert.True(series.Buckets[^1].IncludesToday);
        Assert.False(Day(series, new(2026, 9, 4)).IsPartial);
        Assert.Equal(1_000, series.GrossGil);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(365)]
    public void GrossMatchesIncomeSummaryWithoutChangingSalvageVoyageCounts(int days)
    {
        var period = days == 0 ? (TimeSpan?)null : TimeSpan.FromDays(days);
        var boundary = Now.AddDays(-(days == 0 ? 800 : days));
        FcState[] fcs =
        [
            Fc(1, (1, boundary.AddTicks(-1), 3_000), (1, boundary, 5_000),
                (2, Now.AddDays(-1), 7_000), (2, Now.AddHours(-1), 0), (1, Now.AddSeconds(1), 99_999)),
            Fc(2, (1, Now, 11_000)),
        ];
        var series = Build(fcs, period);
        var metrics = fcs.Select(fc => IncomeMetricsCalculator.Calculate(fc, Now, period)).ToArray();
        var summary = IncomeMetricsCalculator.Summarize(metrics, Now, period);
        Assert.Equal(summary.GrossGil, series.GrossGil);
        Assert.Equal(summary.VoyageCount + 1, series.Buckets.Sum(bucket => bucket.RecordedReturns));
        Assert.Equal(days == 0 ? 26_000 : 23_000, series.GrossGil);
        Assert.DoesNotContain(series.Days, day => day.GrossGil >= 99_999);
    }

    [Theory]
    [InlineData(IncomeHistoryReadStatus.Available, false)]
    [InlineData(IncomeHistoryReadStatus.Unavailable, true)]
    [InlineData(IncomeHistoryReadStatus.Unknown, true)]
    internal void EmptyAndUnavailableHistoryRemainDistinct(IncomeHistoryReadStatus status, bool unavailable)
    {
        var fc = Fc(1) with { IncomeHistory = new(status, status == IncomeHistoryReadStatus.Available ? null : "Test reason") };
        var series = Build([fc], TimeSpan.FromDays(7));
        Assert.Empty(series.Buckets);
        Assert.False(series.HasRecordedReturns);
        Assert.Equal(unavailable, series.HistoryUnavailable);
        Assert.Equal(unavailable ? 1 : 0, series.HistoryNotices.Count);
    }

    [Fact]
    public void PartialHistoryPreservesObservedTotalsAndIdentifiesUnavailableFcs()
    {
        var unavailable = Fc(2) with { IncomeHistory = new(IncomeHistoryReadStatus.Unavailable, "Missing table") };
        var series = Build([Fc(1, (1, Now.AddDays(-1), 4_000)), unavailable], TimeSpan.FromDays(7));
        Assert.False(series.HistoryUnavailable);
        Assert.Equal(1, series.AvailableFcCount);
        Assert.Equal(2, series.FcCount);
        Assert.Contains("Missing table", Assert.Single(series.HistoryNotices));
        Assert.Equal(4_000, series.GrossGil);
    }

    [Fact]
    public void MixedFleetRetainsAllSubmarineHistoryForEitherRoleAndScopeOverridesMembership()
    {
        var mixed = Fc(1, (1, Now, 100), (2, Now, 200), (3, Now, 300));
        var other = Fc(2, (1, Now, 9_000));
        var projection = new FcOperationalProjection(mixed, null, 90, FleetMode.Farming, [], null, null, null)
        { RoleSummary = new(1, 1, 1) }; // Farming, leveling, and paused companions.
        FcState[] source = [mixed, other];
        foreach (var mode in new[] { FleetMode.Farming, FleetMode.Leveling })
        {
            Assert.True(FleetPresentationFiltering.Includes(projection, mode));
            var chart = new IncomeChartCache().Get(true, source, [mixed.FcIdKey], TimeSpan.FromDays(30), Now, TimeZoneInfo.Utc)!;
            Assert.Equal(600, chart.GrossGil);
            Assert.Equal(3, chart.Buckets.Sum(bucket => bucket.RecordedReturns));
            Assert.Equal(1, chart.FcCount);
        }
        var scoped = new IncomeChartCache().Get(true, source, [other.FcIdKey], TimeSpan.FromDays(30), Now, TimeZoneInfo.Utc)!;
        Assert.Equal(9_000, scoped.GrossGil);
    }

    [Fact]
    public void StaggeredFcsDoNotInventObservationsForMissingCompanions()
    {
        var chart = Build([Fc(1, (1, Now.AddDays(-4), 100)), Fc(2, (1, Now.AddDays(-1), 200))], TimeSpan.FromDays(7));
        Assert.Equal(1, Day(chart, new(2026, 9, 2)).FcCount);
        Assert.Equal(1, Day(chart, new(2026, 9, 5)).FcCount);
        Assert.Equal(2, chart.FcCount);
        Assert.Equal(2, chart.Days.Count);
        Assert.Equal(6, chart.Buckets.Count(bucket => bucket.State == IncomeChartBucketState.NoRecordedReturns));
    }

    [Theory]
    [InlineData(2026, 3, 29, 23)]
    [InlineData(2026, 10, 25, 25)]
    public void LocalCalendarDaysHandleDstWithoutLosingOrDuplicatingReturns(int year, int month, int day, int hours)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
        var date = new DateOnly(year, month, day);
        var midnight = IncomeChartSeriesBuilder.StartOfDayUtc(date, zone);
        var next = IncomeChartSeriesBuilder.StartOfDayUtc(date.AddDays(1), zone);
        Assert.Equal(hours, (next - midnight).TotalHours);
        // These straddle the spring jump or the repeated autumn hour.
        var first = new DateTimeOffset(year, month, day, 0, 30, 0, TimeSpan.Zero);
        var second = first.AddHours(1);
        var fc = Fc(1, (1, first, 100), (1, second, 200));
        var series = IncomeChartSeriesBuilder.Build([fc], TimeSpan.FromDays(2), next.AddHours(12), zone);
        var bucket = Day(series, date);
        Assert.Equal(300, bucket.GrossGil);
        Assert.Equal(2, bucket.RecordedReturns);
    }

    [Fact]
    public void LocalMidnightSeparatesReturnsOnTheirLocalDates()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
        var midnight = new DateTimeOffset(2026, 9, 6, 22, 0, 0, TimeSpan.Zero);
        var series = IncomeChartSeriesBuilder.Build([Fc(1, (1, midnight.AddTicks(-1), 100), (1, midnight, 200))],
            TimeSpan.FromDays(1), midnight.AddHours(1), zone);
        Assert.Equal(100, Day(series, new(2026, 9, 6)).GrossGil);
        Assert.Equal(200, Day(series, new(2026, 9, 7)).GrossGil);
    }

    [Fact]
    public void CompleteFirstMidnightIsNotMarkedPartial()
    {
        var now = new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);
        var series = IncomeChartSeriesBuilder.Build([Fc(1, (1, now.AddDays(-7), 100))], TimeSpan.FromDays(7), now, TimeZoneInfo.Utc);
        Assert.False(series.Buckets[0].IsPartial);
        Assert.True(series.Buckets[^1].IsPartial);
    }

    [Theory]
    [InlineData(7, IncomeChartGrouping.Daily)]
    [InlineData(30, IncomeChartGrouping.Daily)]
    [InlineData(90, IncomeChartGrouping.Daily)]
    [InlineData(365, IncomeChartGrouping.Weekly)]
    internal void SelectedPeriodsUseExpectedGrouping(int days, IncomeChartGrouping expected)
    {
        var series = Build([Fc(1, (1, Now, 100))], TimeSpan.FromDays(days));
        Assert.Equal(expected, series.Grouping);
        Assert.InRange(series.Buckets.Count, 1, IncomeChartSeriesBuilder.MaximumBars);
    }

    [Theory]
    [InlineData(90, IncomeChartGrouping.Daily)]
    [InlineData(91, IncomeChartGrouping.Weekly)]
    [InlineData(365, IncomeChartGrouping.Weekly)]
    [InlineData(366, IncomeChartGrouping.Monthly)]
    internal void LifetimeChoosesGroupingFromHistorySpan(int days, IncomeChartGrouping expected)
    {
        var series = Build([Fc(1, (1, Now.AddDays(-days), 100))], null);
        Assert.Equal(expected, series.Grouping);
    }

    [Fact]
    public void WeeklyBucketsStartMondayAndKeepGapCounts()
    {
        var chart = Build([Fc(1, (1, Now.AddDays(-6), 100), (1, Now, 200))], TimeSpan.FromDays(365));
        Assert.All(chart.Buckets.Skip(1), bucket => Assert.Equal(DayOfWeek.Monday, bucket.StartDate.DayOfWeek));
        var last = chart.Buckets[^1];
        Assert.Equal(new DateOnly(2026, 8, 31), last.StartDate);
        Assert.Equal(300, last.GrossGil);
        Assert.Equal(2, last.DaysWithReturns);
        Assert.Equal(5, last.DaysWithoutReturns);
        Assert.True(last.IsPartial);
    }

    [Fact]
    public void MonthlyBucketsKeepLeapDayAndMonthBoundarySeparate()
    {
        var chart = Build([Fc(1,
            (1, new(2024, 1, 31, 12, 0, 0, TimeSpan.Zero), 100),
            (1, new(2024, 2, 29, 23, 59, 59, TimeSpan.Zero), 200),
            (1, new(2024, 3, 1, 0, 0, 0, TimeSpan.Zero), 300))], null);
        Assert.Equal(IncomeChartGrouping.Monthly, chart.Grouping);
        var february = chart.Buckets.Single(bucket => bucket.StartDate == new DateOnly(2024, 2, 1));
        Assert.Equal(29, february.DaysInRange);
        Assert.Equal(28, february.DaysWithoutReturns);
        Assert.Equal(200, february.GrossGil);
        Assert.Equal(300, chart.Buckets.Single(bucket => bucket.StartDate == new DateOnly(2024, 3, 1)).GrossGil);
        Assert.Equal(600, chart.GrossGil);
    }

    [Fact]
    public void LongLifetimeCapsBarsWithoutDroppingObservations()
    {
        var chart = Build([Fc(1, (1, new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero), 100), (1, Now, 200))], null);
        Assert.InRange(chart.Buckets.Count, 1, 120);
        Assert.True(chart.MonthsPerBar > 1);
        Assert.Contains("months per bar", chart.Title);
        Assert.Equal(300, chart.GrossGil);
        Assert.Equal(2, chart.Days.Count); // No huge dense daily array for long empty histories.
    }

    [Fact]
    public void ZeroAndLargeValuesHaveFiniteNonzeroAxisRanges()
    {
        var zero = Build([Fc(1, (1, Now, 0))], TimeSpan.FromDays(7));
        Assert.True(zero.HasRecordedReturns);
        Assert.Equal(1d, zero.AxisMaximum);
        var large = Build([Fc(1, (1, Now, 12_345_678_901))], TimeSpan.FromDays(7));
        Assert.True(double.IsFinite(large.AxisMaximum));
        Assert.True(large.AxisMaximum > large.GrossGil);
    }

    [Fact]
    public void CacheReusesAcrossScopeOrderingAndDoesNoWorkWhenCollapsed()
    {
        FcState[] source = [Fc(1, (1, Now, 100)), Fc(2, (1, Now, 200))];
        var cache = new IncomeChartCache();
        Assert.Null(cache.Get(false, source, source.Select(fc => fc.FcIdKey), null, Now, TimeZoneInfo.Utc));
        Assert.Equal(0, cache.BuildCount);
        var first = cache.Get(true, source, source.Select(fc => fc.FcIdKey), null, Now, TimeZoneInfo.Utc);
        var reordered = cache.Get(true, source, source.Reverse().Select(fc => fc.FcIdKey), null, Now.AddSeconds(10), TimeZoneInfo.Utc);
        Assert.Same(first, reordered); // Favorite and sorting order are not cache inputs.
        Assert.Equal(1, cache.BuildCount);
        Assert.Null(cache.Get(false, source, [], null, Now.AddMinutes(3), TimeZoneInfo.Utc));
        Assert.Equal(1, cache.BuildCount);
    }

    [Fact]
    public void HistoryOnlyChangeInvalidatesCacheEvenWhenForecastFingerprintIsUnchanged()
    {
        FcState[] original = [Fc(1, (1, Now, 100))];
        FcState[] changed = [Fc(1, (1, Now, 500))];
        Assert.Equal(FcDataFingerprint.Create(original[0]), FcDataFingerprint.Create(changed[0]));
        var cache = new IncomeChartCache();
        var before = cache.Get(true, original, [original[0].FcIdKey], null, Now, TimeZoneInfo.Utc)!;
        var after = cache.Get(true, changed, [changed[0].FcIdKey], null, Now, TimeZoneInfo.Utc)!;
        Assert.NotSame(before, after);
        Assert.Equal(500, after.GrossGil);
    }

    [Fact]
    public void ScopePeriodTimezoneAndMinuteExpiryInvalidateCache()
    {
        FcState[] source = [Fc(1, (1, Now, 100)), Fc(2, (1, Now, 200))];
        var cache = new IncomeChartCache();
        var first = cache.Get(true, source, [source[0].FcIdKey], null, Now, TimeZoneInfo.Utc);
        var scoped = cache.Get(true, source, [source[1].FcIdKey], null, Now, TimeZoneInfo.Utc);
        var windowed = cache.Get(true, source, [source[1].FcIdKey], TimeSpan.FromDays(7), Now, TimeZoneInfo.Utc);
        var zoned = cache.Get(true, source, [source[1].FcIdKey], TimeSpan.FromDays(7), Now,
            TimeZoneInfo.CreateCustomTimeZone("UTC", TimeSpan.FromHours(2), "Changed UTC", "Changed UTC"));
        Assert.NotSame(first, scoped);
        Assert.NotSame(scoped, windowed);
        Assert.NotSame(windowed, zoned);
        var expired = cache.Get(true, source, [source[1].FcIdKey], TimeSpan.FromDays(7), Now.AddMinutes(1), TimeZoneInfo.Utc);
        var again = cache.Get(true, source, [source[1].FcIdKey], TimeSpan.FromDays(7), Now.AddMinutes(2), TimeZoneInfo.Utc);
        Assert.NotSame(expired, again);
        Assert.Equal(6, cache.BuildCount);
    }

    [Fact]
    public void CacheInvalidatesAtLocalMidnightBeforeOneMinute()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
        var midnight = IncomeChartSeriesBuilder.StartOfDayUtc(new(2026, 9, 7), zone);
        FcState[] source = [Fc(1, (1, midnight.AddHours(-1), 100))];
        var cache = new IncomeChartCache();
        var before = cache.Get(true, source, [source[0].FcIdKey], TimeSpan.FromDays(7), midnight.AddSeconds(-20), zone)!;
        Assert.Equal(midnight, before.NextRefreshAtUtc);
        Assert.Same(before, cache.Get(true, source, [source[0].FcIdKey], TimeSpan.FromDays(7), midnight.AddTicks(-1), zone));
        var after = cache.Get(true, source, [source[0].FcIdKey], TimeSpan.FromDays(7), midnight, zone)!;
        Assert.NotSame(before, after);
        Assert.Equal(new DateOnly(2026, 9, 7), after.Buckets[^1].EndDate);
    }

    [Fact]
    public void RollingExpiryRemainsInclusiveUntilTheNextTick()
    {
        var period = TimeSpan.FromDays(7);
        var expiry = Now.AddSeconds(20);
        FcState[] source = [Fc(1, (1, expiry - period, 100))];
        var cache = new IncomeChartCache();
        var before = cache.Get(true, source, [source[0].FcIdKey], period, Now, TimeZoneInfo.Utc)!;
        Assert.Equal(expiry.AddTicks(1), before.NextRefreshAtUtc);
        Assert.Same(before, cache.Get(true, source, [source[0].FcIdKey], period, expiry, TimeZoneInfo.Utc));
        var after = cache.Get(true, source, [source[0].FcIdKey], period, expiry.AddTicks(1), TimeZoneInfo.Utc)!;
        Assert.False(after.HasRecordedReturns);
    }

    [Fact]
    public void FutureObservationEntersCacheExactlyAtReturnTime()
    {
        var returnTime = Now.AddSeconds(20);
        FcState[] source = [Fc(1, (1, returnTime, 100))];
        var cache = new IncomeChartCache();
        var before = cache.Get(true, source, [source[0].FcIdKey], null, Now, TimeZoneInfo.Utc)!;
        Assert.False(before.HasRecordedReturns);
        Assert.Equal(returnTime, before.NextRefreshAtUtc);
        var after = cache.Get(true, source, [source[0].FcIdKey], null, returnTime, TimeZoneInfo.Utc)!;
        Assert.Equal(100, after.GrossGil);
    }

    [Fact]
    public void MovingClockBackwardsInvalidatesCache()
    {
        FcState[] source = [Fc(1, (1, Now, 100))];
        var cache = new IncomeChartCache();
        var before = cache.Get(true, source, [source[0].FcIdKey], null, Now, TimeZoneInfo.Utc)!;
        var after = cache.Get(true, source, [source[0].FcIdKey], null, Now.AddTicks(-1), TimeZoneInfo.Utc)!;
        Assert.NotSame(before, after);
        Assert.False(after.HasRecordedReturns);
    }

    private static IncomeChartSeries Build(IReadOnlyList<FcState> fcs, TimeSpan? period)
        => IncomeChartSeriesBuilder.Build(fcs, period, Now, TimeZoneInfo.Utc);

    private static IncomeChartBucket Day(IncomeChartSeries series, DateOnly date)
        => series.Buckets.Single(bucket => bucket.StartDate == date && bucket.EndDate == date);

    private static FcState Fc(byte id, params (int Submarine, DateTimeOffset Return, long Gil)[] records)
    {
        byte[] fcId = [id];
        var key = Convert.ToHexString(fcId);
        var submarines = records.GroupBy(record => record.Submarine).Select(group =>
        {
            var voyages = group.Select(record => new VoyageObservation(key, null, record.Submarine, record.Return,
                [1], 90, 0, 0, 0, record.Gil == 0 ? [] : [new(22500, "Salvage", 1, record.Gil)])).ToArray();
            return new SubmarineState(fcId, group.Key, $"Sub {group.Key}", 90, 0, 100, SubmarineBuildParts.Empty,
                Now.AddHours(1), [], false, [])
            {
                VoyageHistory = voyages,
                Salvage = SubmarineSalvageSummary.Empty with
                { Voyages = voyages.Select(v => new SalvageVoyageRecord(key, group.Key, v.ReturnAtUtc, v.Items)).ToArray() },
            };
        }).ToArray();
        return new FcState(fcId, $"FC{id}", "World", new HashSet<uint>(), new HashSet<uint>(), submarines)
        { IncomeHistory = IncomeHistoryReadState.Available };
    }
}
