namespace SubmarineEtaPlanner.Planner;

internal enum IncomeHistoryReadStatus { Unknown, Available, Unavailable }

internal sealed record IncomeHistoryReadState(IncomeHistoryReadStatus Status, string? Reason = null)
{
    public static readonly IncomeHistoryReadState Unknown = new(IncomeHistoryReadStatus.Unknown, "History availability is unknown.");
    public static readonly IncomeHistoryReadState Available = new(IncomeHistoryReadStatus.Available);
}

internal enum IncomeChartGrouping { Daily, Weekly, Monthly }
internal enum IncomeChartBucketState { RecordedGil, RecordedZero, NoRecordedReturns }

internal sealed record IncomeChartDay(
    DateOnly Date, long GrossGil, int RecordedReturns, IReadOnlySet<string> FcIds);

internal sealed record IncomeChartBucket(
    DateOnly StartDate, DateOnly EndDate, long GrossGil, int RecordedReturns,
    int FcCount, int DaysWithReturns, int DaysInRange, bool IsPartial, bool IncludesToday)
{
    public int DaysWithoutReturns => DaysInRange - DaysWithReturns;
    public IncomeChartBucketState State => RecordedReturns == 0 ? IncomeChartBucketState.NoRecordedReturns
        : GrossGil == 0 ? IncomeChartBucketState.RecordedZero : IncomeChartBucketState.RecordedGil;
}

internal sealed record IncomeChartSeries(
    IncomeChartGrouping Grouping, int MonthsPerBar, IReadOnlyList<IncomeChartDay> Days,
    IReadOnlyList<IncomeChartBucket> Buckets, int FcCount, int AvailableFcCount,
    IReadOnlyList<string> HistoryNotices, DateTimeOffset NextRefreshAtUtc)
{
    public string Title => Grouping switch
    {
        IncomeChartGrouping.Weekly => "Weekly recorded income",
        IncomeChartGrouping.Monthly when MonthsPerBar > 1 => $"Recorded income · {MonthsPerBar} months per bar",
        IncomeChartGrouping.Monthly => "Monthly recorded income",
        _ => "Daily recorded income",
    };
    public long GrossGil => Buckets.Sum(bucket => bucket.GrossGil);
    public bool HasRecordedReturns => Days.Count > 0;
    public bool HistoryUnavailable => FcCount > 0 && AvailableFcCount == 0 && !HasRecordedReturns;
    public double AxisMaximum => Math.Max(1d, Buckets.Select(bucket => (double)bucket.GrossGil).DefaultIfEmpty(0d).Max() * 1.1d);
}

internal static class IncomeChartSeriesBuilder
{
    public const int MaximumBars = 120;

    public static IncomeChartSeries Build(
        IReadOnlyList<FcState> freeCompanies, TimeSpan? period, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(freeCompanies);
        ArgumentNullException.ThrowIfNull(timeZone);
        if (period is { } duration && duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(period));

        var today = LocalDate(now, timeZone);
        var windowStart = period is { } span ? now - span : (DateTimeOffset?)null;
        var refreshAt = now.AddMinutes(1);
        ConsiderBoundary(StartOfDayUtc(today.AddDays(1), timeZone));
        var observations = new List<(string FcId, VoyageObservation Voyage)>();
        var notices = new List<string>();
        foreach (var fc in freeCompanies)
        {
            if (fc.IncomeHistory.Status != IncomeHistoryReadStatus.Available)
                notices.Add($"{fc.DisplayName}: {fc.IncomeHistory.Reason ?? "History availability is unknown."}");
            foreach (var voyage in fc.Submarines.SelectMany(submarine => submarine.VoyageHistory))
            {
                if (voyage.ReturnAtUtc > now)
                {
                    ConsiderBoundary(voyage.ReturnAtUtc);
                    continue;
                }
                if (windowStart is { } start && voyage.ReturnAtUtc < start) continue;
                observations.Add((fc.FcIdKey, voyage));
                // Income's lower boundary is inclusive. Expire one tick after it passes a return.
                if (period is { } expiryPeriod && voyage.ReturnAtUtc < DateTimeOffset.MaxValue - expiryPeriod)
                    ConsiderBoundary((voyage.ReturnAtUtc + expiryPeriod).AddTicks(1));
            }
        }

        var days = observations.GroupBy(item => LocalDate(item.Voyage.ReturnAtUtc, timeZone))
            .OrderBy(group => group.Key)
            .Select(group => new IncomeChartDay(group.Key, group.Sum(item => item.Voyage.GrossNpcGil),
                group.Count(), group.Select(item => item.FcId).ToHashSet(StringComparer.OrdinalIgnoreCase)))
            .ToArray();
        var rangeStart = windowStart ?? observations.Select(item => item.Voyage.ReturnAtUtc).DefaultIfEmpty(now).Min();
        var firstDate = LocalDate(rangeStart, timeZone);
        var rangeDays = (now - rangeStart).TotalDays;
        var grouping = rangeDays <= 90 ? IncomeChartGrouping.Daily
            : rangeDays <= 365 ? IncomeChartGrouping.Weekly : IncomeChartGrouping.Monthly;
        var monthCount = (today.Year - firstDate.Year) * 12 + today.Month - firstDate.Month + 1;
        var monthsPerBar = Math.Max(1, (int)Math.Ceiling(monthCount / (double)MaximumBars));
        var buckets = new List<IncomeChartBucket>();
        // Empty ranges have a message, not a misleading row of zero bars.
        if (days.Length > 0)
        {
            var calendarStart = grouping switch
            {
                IncomeChartGrouping.Weekly => firstDate.AddDays(-(((int)firstDate.DayOfWeek + 6) % 7)),
                IncomeChartGrouping.Monthly => new DateOnly(firstDate.Year, firstDate.Month, 1),
                _ => firstDate,
            };
            var dayIndex = 0;
            while (calendarStart <= today)
            {
                var next = grouping switch
                {
                    IncomeChartGrouping.Weekly => calendarStart.AddDays(7),
                    IncomeChartGrouping.Monthly => calendarStart.AddMonths(monthsPerBar),
                    _ => calendarStart.AddDays(1),
                };
                var bucketStart = calendarStart < firstDate ? firstDate : calendarStart;
                var bucketEnd = next.AddDays(-1) > today ? today : next.AddDays(-1);
                long gil = 0;
                var returns = 0;
                var observedDays = 0;
                var fcIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (dayIndex < days.Length && days[dayIndex].Date < next)
                {
                    var day = days[dayIndex++];
                    gil = checked(gil + day.GrossGil);
                    returns += day.RecordedReturns;
                    observedDays++;
                    fcIds.UnionWith(day.FcIds);
                }
                buckets.Add(new(bucketStart, bucketEnd, gil, returns, fcIds.Count, observedDays,
                    bucketEnd.DayNumber - bucketStart.DayNumber + 1,
                    rangeStart > StartOfDayUtc(calendarStart, timeZone) || now < StartOfDayUtc(next, timeZone),
                    bucketEnd == today));
                calendarStart = next;
            }
        }
        return new(grouping, monthsPerBar, days, buckets, freeCompanies.Count,
            freeCompanies.Count(fc => fc.IncomeHistory.Status == IncomeHistoryReadStatus.Available), notices, refreshAt);

        void ConsiderBoundary(DateTimeOffset boundary)
        {
            if (boundary > now && boundary < refreshAt) refreshAt = boundary;
        }
    }

    internal static DateOnly LocalDate(DateTimeOffset time, TimeZoneInfo timeZone)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, timeZone).DateTime);

    internal static DateTimeOffset StartOfDayUtc(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Some zones change offsets at midnight, or skip an entire calendar day.
        while (timeZone.IsInvalidTime(local)) local = local.AddMinutes(1);
        var offset = timeZone.IsAmbiguousTime(local) ? timeZone.GetAmbiguousTimeOffsets(local).Max()
            : timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}

/// <summary>History references, membership, and time boundaries drive work; draw frames do not.</summary>
internal sealed class IncomeChartCache
{
    private IReadOnlyList<FcState>? source;
    private string[] scope = [];
    private TimeSpan? period;
    private TimeZoneInfo? timeZone;
    private DateTimeOffset createdAt;
    private IncomeChartSeries? series;
    internal int BuildCount { get; private set; }

    public IncomeChartSeries? Get(bool visible, IReadOnlyList<FcState> snapshot,
        IEnumerable<string> scopedFcIds, TimeSpan? selectedPeriod, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (!visible) return null;
        var ids = scopedFcIds.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (this.series is not null && ReferenceEquals(this.source, snapshot) && this.period == selectedPeriod
            && this.scope.SequenceEqual(ids, StringComparer.OrdinalIgnoreCase)
            && this.timeZone?.Id == zone.Id && this.timeZone.HasSameRules(zone)
            && now >= this.createdAt && now < this.series.NextRefreshAtUtc)
            return this.series;
        var membership = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        this.series = IncomeChartSeriesBuilder.Build(snapshot.Where(fc => membership.Contains(fc.FcIdKey)).ToArray(),
            selectedPeriod, now, zone);
        this.source = snapshot;
        this.scope = ids;
        this.period = selectedPeriod;
        this.timeZone = zone;
        this.createdAt = now;
        this.BuildCount++;
        return this.series;
    }
}
