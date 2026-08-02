using SubmarineEtaPlanner.TrackerData;

namespace SubmarineEtaPlanner.Planner;

public sealed class EtaPlannerService(
    ISubmarineTrackerStateReader stateReader,
    EtaSimulator simulator,
    IRouteSearchDiagnostics? routeSearchDiagnostics = null)
{
    public SubmarineTrackerDataFingerprint GetDataFingerprint(EtaSettings settings)
        => stateReader.GetDataFingerprint(settings);

    public EtaPlannerSnapshot Calculate(EtaSettings settings, DateTimeOffset now)
        => Calculate(settings, now, CancellationToken.None);

    public EtaPlannerSnapshot Calculate(EtaSettings settings, DateTimeOffset now, CancellationToken cancellationToken)
        => Calculate(settings, now, cancellationToken, null);

    public EtaPlannerSnapshot Calculate(
        EtaSettings settings,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        Action<EtaPlannerSnapshot>? reportProgress)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        routeSearchDiagnostics?.ResetRouteSearchMetrics();
        var warnings = new List<string>();
        var fcStates = stateReader.Read(settings, warnings);
        var results = new Dictionary<string, EtaResult>();
        var progress = fcStates.ToDictionary(
            fc => fc.FcIdKey,
            fc => new FcCalculationProgress(fc.FcIdKey, fc.DisplayName, FcCalculationStatus.Queued));

        EtaPlannerSnapshot CreateSnapshot(bool isRunning)
        {
            var resultArray = fcStates
                .Where(fc => results.ContainsKey(fc.FcIdKey))
                .Select(fc => results[fc.FcIdKey])
                .ToArray();
            var progressArray = fcStates.Select(fc => progress[fc.FcIdKey]).ToArray();
            var status = !isRunning &&
                         resultArray.Length == fcStates.Count &&
                         resultArray.All(result => result.IsComplete) &&
                         warnings.All(warning => !IsIncompleteWarning(warning))
                ? CalculationStatus.Complete
                : CalculationStatus.Partial;
            var reason = isRunning || status == CalculationStatus.Complete
                ? null
                : progressArray.FirstOrDefault(item => item.Status is FcCalculationStatus.TimedOut or FcCalculationStatus.Failed)?.Message ??
                  resultArray.FirstOrDefault(result => !result.IsComplete)?.IncompleteReason ??
                  warnings.FirstOrDefault(IsIncompleteWarning) ??
                  "Calculation stopped before every tracked FC completed.";
            var routeMetrics = routeSearchDiagnostics?.GetRouteSearchMetrics() ?? new RouteSearchMetrics(0, 0, 0);
            return new EtaPlannerSnapshot(
                now,
                fcStates,
                resultArray,
                warnings.ToArray(),
                status,
                reason,
                new CalculationMetrics(stopwatch.ElapsedMilliseconds, routeMetrics.Queries, routeMetrics.CacheHits, routeMetrics.RoutesEvaluated))
            {
                FcProgress = progressArray,
                IsRunning = isRunning,
            };
        }

        void Publish(bool isRunning) => reportProgress?.Invoke(CreateSnapshot(isRunning));

        Publish(isRunning: true);

        var calculationOrder = fcStates
            .OrderBy(fc => IsReadyNow(fc, settings.TargetRank) ? 0 : 1)
            .ThenByDescending(fc => fc.Submarines.Count == 0 ? 0 : fc.Submarines.Min(submarine => submarine.Rank))
            .ThenBy(fc => fc.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var fc in calculationOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startedAtUtc = DateTimeOffset.UtcNow;
            progress[fc.FcIdKey] = progress[fc.FcIdKey] with
            {
                Status = FcCalculationStatus.Calculating,
                StartedAtUtc = startedAtUtc,
                Message = "Calculating probabilistic forecast…",
            };
            Publish(isRunning: true);

            var deadlineUtc = settings.CalculationTimeLimitSeconds > 0
                ? startedAtUtc.AddSeconds(settings.CalculationTimeLimitSeconds)
                : (DateTimeOffset?)null;

            try
            {
                var result = simulator.Simulate(fc, settings, now, deadlineUtc, cancellationToken);
                results[fc.FcIdKey] = result;
                var timedOut = !result.IsComplete &&
                               (IsTimedOut(deadlineUtc) ||
                                result.Warnings.Any(warning => warning.Contains("time limit", StringComparison.OrdinalIgnoreCase)));
                var calculationStatus = timedOut
                    ? FcCalculationStatus.TimedOut
                    : result.IsComplete ? FcCalculationStatus.Complete : FcCalculationStatus.Partial;
                var message = calculationStatus switch
                {
                    FcCalculationStatus.TimedOut =>
                        $"Timed out after {settings.CalculationTimeLimitSeconds} seconds; continuing with the next FC.",
                    FcCalculationStatus.Partial => result.IncompleteReason ?? "Forecast is partial.",
                    _ => $"Completed with {result.ProbabilitySampleCount} probability samples.",
                };
                progress[fc.FcIdKey] = progress[fc.FcIdKey] with
                {
                    Status = calculationStatus,
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    Message = message,
                };
                if (timedOut)
                    warnings.Add($"{fc.DisplayName}: {message}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var message = $"Forecast failed: {ex.Message}";
                warnings.Add($"{fc.DisplayName}: {message}");
                progress[fc.FcIdKey] = progress[fc.FcIdKey] with
                {
                    Status = FcCalculationStatus.Failed,
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    Message = message,
                };
            }

            Publish(isRunning: true);
        }

        var finalSnapshot = CreateSnapshot(isRunning: false);
        reportProgress?.Invoke(finalSnapshot);
        return finalSnapshot;
    }

    private static bool IsTimedOut(DateTimeOffset? deadlineUtc)
        => deadlineUtc is not null && DateTimeOffset.UtcNow >= deadlineUtc.Value;

    private static bool IsIncompleteWarning(string warning)
        => warning.Contains("partial", StringComparison.OrdinalIgnoreCase) ||
           warning.Contains("incomplete", StringComparison.OrdinalIgnoreCase) ||
           warning.Contains("stopped", StringComparison.OrdinalIgnoreCase);

    private static bool IsReadyNow(FcState fc, int targetRank)
        => fc.Submarines.Count > 0 && fc.Submarines.All(submarine => submarine.Rank >= targetRank);
}

public enum FcCalculationStatus
{
    Queued,
    Calculating,
    Complete,
    Partial,
    TimedOut,
    Failed,
    Cancelled,
}

public sealed record FcCalculationProgress(
    string FcIdKey,
    string FcDisplayName,
    FcCalculationStatus Status,
    DateTimeOffset? StartedAtUtc = null,
    DateTimeOffset? CompletedAtUtc = null,
    string? Message = null);

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

    public IReadOnlyList<FcCalculationProgress> FcProgress { get; init; } = [];

    public bool IsRunning { get; init; }
}
