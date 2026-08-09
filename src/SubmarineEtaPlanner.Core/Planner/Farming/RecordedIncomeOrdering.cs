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
                IncomeSort.GilPerDay => metric.GilPerDay,
                IncomeSort.GilPerVoyage => metric.GilPerVoyage,
                IncomeSort.FcName => 0,
                _ => metric.GrossGil,
            })
            .ThenBy(metric => metric.FcDisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
