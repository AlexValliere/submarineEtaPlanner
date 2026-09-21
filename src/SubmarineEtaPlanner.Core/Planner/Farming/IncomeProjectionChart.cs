namespace SubmarineEtaPlanner.Planner;

public sealed record IncomeProjectionChartBucket(
    DateOnly StartDate, DateOnly EndDate, DateTimeOffset StartAtUtc, DateTimeOffset EndAtUtc,
    decimal EstimatedGil, bool IsPartial);

public sealed record IncomeProjectionChartSeries(
    bool Monthly, DateTimeOffset GeneratedAtUtc, DateTimeOffset NextRefreshAtUtc,
    IncomeProjectionTotals Totals, IncomeProjectionHorizon Horizon,
    IReadOnlyList<IncomeProjectionChartBucket> Buckets)
{
    public string Title => Monthly ? "Monthly estimated income" : "Daily estimated income";
    public decimal? EstimatedGil => Totals.GilPerDay is null ? null : Buckets.Sum(bucket => bucket.EstimatedGil);
    public double AxisMaximum => Math.Max(1d, Buckets.Select(bucket => (double)bucket.EstimatedGil).DefaultIfEmpty().Max() * 1.1);
}

public static class IncomeProjectionChartBuilder
{
    public static IncomeProjectionChartSeries Build(IncomeProjectionTotals totals, IncomeProjectionHorizon horizon,
        DateTimeOffset now, TimeZoneInfo timeZone)
    {
        horizon = IncomeProjectionPreferences.Normalize(horizon);
        var end = now.AddDays((int)horizon);
        var monthly = horizon == IncomeProjectionHorizon.Days365;
        var today = IncomeChartSeriesBuilder.LocalDate(now, timeZone);
        var midnight = IncomeChartSeriesBuilder.StartOfDayUtc(today.AddDays(1), timeZone);
        var refreshAt = midnight > now && midnight < now.AddMinutes(1) ? midnight : now.AddMinutes(1);
        var buckets = new List<IncomeProjectionChartBucket>();
        if (totals.GilPerDay is { } rate)
        {
            var date = monthly ? new DateOnly(today.Year, today.Month, 1) : today;
            decimal previousCumulative = 0;
            while (true)
            {
                var nextDate = monthly ? date.AddMonths(1) : date.AddDays(1);
                var calendarStart = IncomeChartSeriesBuilder.StartOfDayUtc(date, timeZone);
                var calendarEnd = IncomeChartSeriesBuilder.StartOfDayUtc(nextDate, timeZone);
                if (calendarStart >= end) break;
                var startAt = calendarStart < now ? now : calendarStart;
                var endAt = calendarEnd > end ? end : calendarEnd;
                if (endAt > startAt)
                {
                    // Difference of cumulative amounts preserves exact summary parity without rounding each bucket.
                    var cumulative = endAt == end ? rate * (int)horizon
                        : Prorate(rate, endAt - now);
                    buckets.Add(new(IncomeChartSeriesBuilder.LocalDate(startAt, timeZone),
                        IncomeChartSeriesBuilder.LocalDate(endAt.AddTicks(-1), timeZone),
                        startAt.ToUniversalTime(), endAt.ToUniversalTime(), cumulative - previousCumulative,
                        startAt > calendarStart || endAt < calendarEnd));
                    previousCumulative = cumulative;
                }
                date = nextDate;
            }
        }
        return new(monthly, now, refreshAt, totals, horizon, buckets.AsReadOnly());
    }

    private static decimal Prorate(decimal rate, TimeSpan elapsed)
    {
        var days = elapsed.Ticks / TimeSpan.TicksPerDay;
        var remainder = elapsed.Ticks % TimeSpan.TicksPerDay;
        // Multiply before division when possible: e.g. 2,400 gil/day for 23 hours is exactly 2,300.
        // Splitting whole days also avoids multiplying a large annual rate by an entire year's ticks.
        var fraction = remainder == 0 ? 0 : rate <= decimal.MaxValue / remainder
            ? rate * remainder / TimeSpan.TicksPerDay
            : rate / TimeSpan.TicksPerDay * remainder;
        return rate * days + fraction;
    }
}

internal sealed class IncomeProjectionChartCache
{
    private IncomeProjectionChartSeries? series;
    private TimeZoneInfo? timeZone;
    internal int BuildCount { get; private set; }

    public IncomeProjectionChartSeries? Get(bool visible, IncomeProjectionTotals totals,
        IncomeProjectionHorizon horizon, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (!visible) return null;
        horizon = IncomeProjectionPreferences.Normalize(horizon);
        if (this.series is not null && this.series.Totals == totals && this.series.Horizon == horizon
            && this.timeZone?.Id == zone.Id && this.timeZone.HasSameRules(zone)
            && now >= this.series.GeneratedAtUtc && now < this.series.NextRefreshAtUtc)
            return this.series;
        this.series = IncomeProjectionChartBuilder.Build(totals, horizon, now, zone);
        this.timeZone = zone;
        this.BuildCount++;
        return this.series;
    }
}
