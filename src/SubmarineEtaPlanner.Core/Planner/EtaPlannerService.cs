using SubmarineEtaPlanner.TrackerData;

namespace SubmarineEtaPlanner.Planner;

public sealed class EtaPlannerService(
    SubmarineTrackerStateReader stateReader,
    EtaSimulator simulator,
    IRouteSearchDiagnostics? routeSearchDiagnostics = null)
{
    public SubmarineTrackerDataFingerprint GetDataFingerprint(EtaSettings settings)
        => stateReader.GetDataFingerprint(settings);

    public EtaPlannerSnapshot Calculate(EtaSettings settings, DateTimeOffset now)
        => Calculate(settings, now, CancellationToken.None);

    public EtaPlannerSnapshot Calculate(EtaSettings settings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        routeSearchDiagnostics?.ResetRouteSearchMetrics();
        var warnings = new List<string>();
        var fcStates = stateReader.Read(settings, warnings);
        var deadlineUtc = settings.CalculationTimeLimitSeconds > 0
            ? DateTimeOffset.UtcNow.AddSeconds(settings.CalculationTimeLimitSeconds)
            : (DateTimeOffset?)null;
        var results = new List<EtaResult>();

        foreach (var fc in fcStates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsTimedOut(deadlineUtc))
            {
                warnings.Add($"Calculation stopped after {settings.CalculationTimeLimitSeconds} seconds. Results are partial.");
                break;
            }

            results.Add(simulator.Simulate(fc, settings, now, deadlineUtc, cancellationToken));
        }

        var status = results.Count == fcStates.Count && results.All(r => r.IsComplete) && warnings.All(w => !IsIncompleteWarning(w))
            ? CalculationStatus.Complete
            : CalculationStatus.Partial;
        var reason = status == CalculationStatus.Complete
            ? null
            : results.FirstOrDefault(r => !r.IsComplete)?.IncompleteReason ??
              warnings.FirstOrDefault(IsIncompleteWarning) ??
              "Calculation stopped before every tracked FC completed.";

        var routeMetrics = routeSearchDiagnostics?.GetRouteSearchMetrics() ?? new RouteSearchMetrics(0, 0, 0);
        return new EtaPlannerSnapshot(
            now,
            fcStates,
            results,
            warnings,
            status,
            reason,
            new CalculationMetrics(stopwatch.ElapsedMilliseconds, routeMetrics.Queries, routeMetrics.CacheHits, routeMetrics.RoutesEvaluated));
    }

    private static bool IsTimedOut(DateTimeOffset? deadlineUtc)
        => deadlineUtc is not null && DateTimeOffset.UtcNow >= deadlineUtc.Value;

    private static bool IsIncompleteWarning(string warning)
        => warning.Contains("partial", StringComparison.OrdinalIgnoreCase) ||
           warning.Contains("incomplete", StringComparison.OrdinalIgnoreCase) ||
           warning.Contains("stopped", StringComparison.OrdinalIgnoreCase);
}

public sealed record EtaPlannerSnapshot(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<FcState> FreeCompanies,
    IReadOnlyList<EtaResult> Results,
    IReadOnlyList<string> Warnings,
    CalculationStatus Status,
    string? IncompleteReason,
    CalculationMetrics? Metrics = null)
{
    public bool IsComplete => Status == CalculationStatus.Complete;
}
