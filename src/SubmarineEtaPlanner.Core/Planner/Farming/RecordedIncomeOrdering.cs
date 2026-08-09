namespace SubmarineEtaPlanner.Planner;

public static class IncomeMetricsOrdering
{
    public static IReadOnlyList<IncomeFcMetrics> Order(
        IEnumerable<IncomeFcMetrics> metrics,
        IncomeSort sort,
        Func<IncomeFcMetrics, bool> isFavorite)
        => metrics
            .OrderByDescending(isFavorite)
            .ThenByDescending(metric => sort switch
            {
                IncomeSort.ObservedRunRateGilPerDay => metric.ObservedRunRateGilPerDay,
                IncomeSort.RecordedAverageGilPerDay => metric.RecordedAverageGilPerDay,
                IncomeSort.GilPerVoyage => metric.GilPerVoyage,
                IncomeSort.FcName => 0,
                _ => metric.GrossGil,
            })
            .ThenBy(metric => metric.FcDisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
