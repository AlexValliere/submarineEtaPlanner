namespace SubmarineEtaPlanner.Planner;

public static class IncomeMetricsCalculator
{
    public static IncomeFcMetrics Calculate(FcState fc, DateTimeOffset now, TimeSpan? period)
        => CalculateCore(fc, now, period, catalog: null);

    public static IncomeFcMetrics Calculate(
        FcState fc,
        DateTimeOffset now,
        TimeSpan? period,
        ISubmarineCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return CalculateCore(fc, now, period, catalog);
    }

    private static IncomeFcMetrics CalculateCore(
        FcState fc,
        DateTimeOffset now,
        TimeSpan? period,
        ISubmarineCatalog? catalog)
    {
        var windowStart = period is null ? (DateTimeOffset?)null : now - period.Value;
        var submarines = fc.Submarines.Select(submarine =>
        {
            var currentBuild = catalog is null
                ? CurrentBuildPresentation.NotResolved
                : CurrentBuildPresentation.Create(catalog.ResolveBuild(submarine.BuildParts, submarine.Rank));
            var voyages = submarine.Salvage.Voyages
                .Where(voyage => voyage.ReturnAtUtc <= now && (windowStart is null || voyage.ReturnAtUtc >= windowStart))
                .OrderBy(voyage => voyage.ReturnAtUtc)
                .ToArray();
            var first = voyages.FirstOrDefault()?.ReturnAtUtc;
            var last = voyages.LastOrDefault()?.ReturnAtUtc;
            var coveredStart = first is null ? (DateTimeOffset?)null : windowStart is null ? first : Max(first.Value, windowStart.Value);
            var coveredDays = coveredStart is null ? 0d : Math.Max((now - coveredStart.Value).TotalDays, 1d / 24d);
            var gil = voyages.Sum(voyage => voyage.GrossNpcGil);
            return new IncomeSubmarineMetrics(
                submarine.SubmarineId,
                submarine.Name,
                gil,
                voyages.Length,
                coveredDays <= 0 ? 0 : gil / coveredDays,
                voyages.Length == 0 ? 0 : gil / (double)voyages.Length,
                first,
                last)
            {
                Rank = submarine.Rank,
                CurrentBuild = currentBuild,
            };
        }).ToArray();
        var fcFirst = submarines.Where(item => item.FirstReturnAtUtc is not null).Select(item => item.FirstReturnAtUtc).Min();
        var fcLast = submarines.Where(item => item.LastReturnAtUtc is not null).Select(item => item.LastReturnAtUtc).Max();
        var fcCoveredStart = fcFirst is null ? (DateTimeOffset?)null : windowStart is null ? fcFirst : Max(fcFirst.Value, windowStart.Value);
        var fcCoveredDays = fcCoveredStart is null ? 0d : Math.Max((now - fcCoveredStart.Value).TotalDays, 1d / 24d);
        var gross = submarines.Sum(item => item.GrossGil);
        var voyageCount = submarines.Sum(item => item.ValidVoyages);
        return new IncomeFcMetrics(
            fc.FcIdKey,
            fc.DisplayName,
            gross,
            voyageCount,
            fcCoveredDays <= 0 ? 0 : gross / fcCoveredDays,
            voyageCount == 0 ? 0 : gross / (double)voyageCount,
            fcCoveredDays,
            fcFirst,
            fcLast,
            submarines);
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;

    public static IncomeSummaryMetrics Summarize(
        IReadOnlyList<IncomeFcMetrics> metrics,
        DateTimeOffset now,
        TimeSpan? period)
    {
        var gross = metrics.Sum(item => item.GrossGil);
        var voyages = metrics.Sum(item => item.ValidVoyages);
        var first = metrics
            .Where(item => item.FirstReturnAtUtc is not null)
            .Select(item => item.FirstReturnAtUtc)
            .Min();
        var start = first is null
            ? (DateTimeOffset?)null
            : period is null
                ? first
                : first > now - period ? first : now - period;
        var days = start is null ? 0 : Math.Max((now - start.Value).TotalDays, 1d / 24d);
        return new IncomeSummaryMetrics(
            gross,
            voyages,
            days,
            days == 0 ? 0 : gross / days,
            voyages == 0 ? 0 : gross / (double)voyages,
            metrics.Count);
    }
}
