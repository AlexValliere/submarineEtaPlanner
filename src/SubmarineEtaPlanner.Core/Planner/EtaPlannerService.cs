using SubmarineEtaPlanner.TrackerData;

namespace SubmarineEtaPlanner.Planner;

public sealed class EtaPlannerService(
    ISubmarineTrackerStateReader stateReader,
    IEtaSimulator simulator,
    IRouteSearchDiagnostics? routeSearchDiagnostics = null,
    IPlannerDataDiagnostics? dataDiagnostics = null,
    int maximumRank = int.MaxValue)
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
        => Calculate(settings, now, cancellationToken, reportProgress, null, ForecastRefreshMode.Full);

    public EtaPlannerSnapshot Calculate(
        EtaSettings settings,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        Action<EtaPlannerSnapshot>? reportProgress,
        EtaPlannerSnapshot? previousSnapshot,
        ForecastRefreshMode refreshMode)
        => Calculate(
            PlannerCalculationRequest.FromGlobalSettings(settings),
            now,
            cancellationToken,
            reportProgress,
            previousSnapshot,
            refreshMode);

    public EtaPlannerSnapshot Calculate(
        PlannerCalculationRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        Action<EtaPlannerSnapshot>? reportProgress = null,
        EtaPlannerSnapshot? previousSnapshot = null,
        ForecastRefreshMode refreshMode = ForecastRefreshMode.Full)
    {
        var settings = request.GlobalSettings;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        routeSearchDiagnostics?.ResetRouteSearchMetrics();
        var warnings = new List<string>();
        if (dataDiagnostics is not null)
            warnings.AddRange(dataDiagnostics.GetPlannerDataWarnings());
        var fcStates = stateReader.Read(settings, warnings)
            .Select(EnsureFingerprint)
            .ToArray();
        var settingsFingerprint = CalculationSettingsFingerprint.Create(settings);
        var effectiveSettings = fcStates.ToDictionary(
            fc => fc.FcIdKey,
            fc => EffectiveEtaSettingsResolver.Resolve(
                settings,
                request.FreeCompanyOverrides.TryGetValue(fc.FcIdKey, out var simulationOverride) ? simulationOverride : null,
                maximumRank),
            StringComparer.OrdinalIgnoreCase);
        var effectiveSettingsFingerprints = effectiveSettings.ToDictionary(
            pair => pair.Key,
            pair => CalculationSettingsFingerprint.Create(pair.Value),
            StringComparer.OrdinalIgnoreCase);
        var results = new Dictionary<string, EtaResult>();
        var progress = new Dictionary<string, FcCalculationProgress>();
        var calculatedCount = 0;
        var reusedCount = 0;
        var awaitingTrackerCount = 0;

        var canReuse = refreshMode == ForecastRefreshMode.Incremental && previousSnapshot is not null;
        var previousStates = canReuse
            ? previousSnapshot!.FreeCompanies.ToDictionary(fc => fc.FcIdKey)
            : new Dictionary<string, FcState>();
        var previousResults = canReuse
            ? previousSnapshot!.Results.ToDictionary(result => Convert.ToHexString(result.FcId))
            : new Dictionary<string, EtaResult>();
        var previousProgress = canReuse
            ? previousSnapshot!.FcProgress.ToDictionary(item => item.FcIdKey)
            : new Dictionary<string, FcCalculationProgress>();

        foreach (var fc in fcStates)
        {
            if (CanReuseCompletedResult(
                    fc,
                    effectiveSettings[fc.FcIdKey].TargetRank,
                    effectiveSettingsFingerprints[fc.FcIdKey],
                    now,
                    previousSnapshot,
                    previousStates,
                    previousResults,
                    previousProgress,
                    out var previousResult,
                    out var awaitingTracker))
            {
                results[fc.FcIdKey] = previousResult!;
                if (awaitingTracker)
                {
                    awaitingTrackerCount++;
                    progress[fc.FcIdKey] = new FcCalculationProgress(
                        fc.FcIdKey,
                        fc.DisplayName,
                        FcCalculationStatus.AwaitingTrackerUpdate,
                        CompletedAtUtc: now,
                        Message: "Collect returned submarines in-game. If already collected, wait for SubmarineTracker to record the result.");
                }
                else
                {
                    reusedCount++;
                    progress[fc.FcIdKey] = new FcCalculationProgress(
                        fc.FcIdKey,
                        fc.DisplayName,
                        FcCalculationStatus.Reused,
                        CompletedAtUtc: now,
                        Message: $"Up to date; reused forecast from {previousResult!.GeneratedAtUtc.LocalDateTime:g}.");
                }
            }
            else
            {
                progress[fc.FcIdKey] = new FcCalculationProgress(fc.FcIdKey, fc.DisplayName, FcCalculationStatus.Queued);
            }
        }

        EtaPlannerSnapshot CreateSnapshot(bool isRunning)
        {
            var resultArray = fcStates
                .Where(fc => results.ContainsKey(fc.FcIdKey))
                .Select(fc => results[fc.FcIdKey])
                .ToArray();
            var progressArray = fcStates.Select(fc => progress[fc.FcIdKey]).ToArray();
            var hasIncompleteProgress = progressArray.Any(item => item.Status is
                FcCalculationStatus.Partial or
                FcCalculationStatus.TimedOut or
                FcCalculationStatus.Failed or
                FcCalculationStatus.Cancelled or
                FcCalculationStatus.AwaitingTrackerUpdate);
            var status = !isRunning &&
                         !hasIncompleteProgress &&
                         resultArray.Length == fcStates.Length &&
                         resultArray.All(result => result.IsComplete) &&
                         warnings.All(warning => !IsIncompleteWarning(warning))
                ? CalculationStatus.Complete
                : CalculationStatus.Partial;
            var reason = isRunning || status == CalculationStatus.Complete
                ? null
                : progressArray.FirstOrDefault(item => item.Status == FcCalculationStatus.AwaitingTrackerUpdate)?.Message ??
                  progressArray.FirstOrDefault(item => item.Status is FcCalculationStatus.TimedOut or FcCalculationStatus.Failed)?.Message ??
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
                new CalculationMetrics(
                    stopwatch.ElapsedMilliseconds,
                    routeMetrics.Queries,
                    routeMetrics.CacheHits,
                    routeMetrics.RoutesEvaluated,
                    calculatedCount,
                    reusedCount,
                    awaitingTrackerCount,
                    routeMetrics.RankingBuilds,
                    routeMetrics.RankingCacheHits,
                    routeMetrics.RankedRoutesEvaluated,
                    routeMetrics.ExhaustiveRoutesEvaluated,
                    routeMetrics.RankingBuildMilliseconds,
                    routeMetrics.ExactCacheEvictions,
                    routeMetrics.RankingCacheEvictions))
            {
                FcProgress = progressArray,
                IsRunning = isRunning,
                CalculationSettingsFingerprint = settingsFingerprint,
                FcCalculationSettingsFingerprints = effectiveSettingsFingerprints,
                RefreshMode = refreshMode,
            };
        }

        void Publish(bool isRunning) => reportProgress?.Invoke(CreateSnapshot(isRunning));

        Publish(isRunning: true);

        var calculationOrder = fcStates
            .Where(fc => progress[fc.FcIdKey].Status == FcCalculationStatus.Queued)
            .OrderBy(fc => IsReadyNow(fc, effectiveSettings[fc.FcIdKey].TargetRank) ? 0 : 1)
            .ThenByDescending(fc => fc.Submarines.Count == 0 ? 0 : fc.Submarines.Min(submarine => submarine.Rank))
            .ThenBy(fc => fc.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var fc in calculationOrder)
        {
            var fcSettings = effectiveSettings[fc.FcIdKey];
            cancellationToken.ThrowIfCancellationRequested();
            var startedAtUtc = DateTimeOffset.UtcNow;
            progress[fc.FcIdKey] = progress[fc.FcIdKey] with
            {
                Status = FcCalculationStatus.Calculating,
                StartedAtUtc = startedAtUtc,
                Message = "Calculating probabilistic forecast…",
            };
            Publish(isRunning: true);

            var deadlineUtc = fcSettings.CalculationTimeLimitSeconds > 0
                ? startedAtUtc.AddSeconds(fcSettings.CalculationTimeLimitSeconds)
                : (DateTimeOffset?)null;

            try
            {
                var result = simulator.Simulate(fc, fcSettings, now, deadlineUtc, cancellationToken);
                calculatedCount++;
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
                        $"Timed out after {fcSettings.CalculationTimeLimitSeconds} seconds; continuing with the next FC.",
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
                calculatedCount++;
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

    private static FcState EnsureFingerprint(FcState fc)
        => fc.DataFingerprint.IsEmpty ? fc with { DataFingerprint = FcDataFingerprint.Create(fc) } : fc;

    private static bool CanReuseCompletedResult(
        FcState fc,
        int targetRank,
        CalculationSettingsFingerprint effectiveSettingsFingerprint,
        DateTimeOffset now,
        EtaPlannerSnapshot? previousSnapshot,
        IReadOnlyDictionary<string, FcState> previousStates,
        IReadOnlyDictionary<string, EtaResult> previousResults,
        IReadOnlyDictionary<string, FcCalculationProgress> previousProgress,
        out EtaResult? previousResult,
        out bool awaitingTracker)
    {
        previousResult = null;
        awaitingTracker = false;
        if (previousSnapshot is null ||
            !previousSnapshot.FcCalculationSettingsFingerprints.TryGetValue(fc.FcIdKey, out var previousSettingsFingerprint) ||
            previousSettingsFingerprint != effectiveSettingsFingerprint ||
            !previousStates.TryGetValue(fc.FcIdKey, out var previousState) ||
            previousState.DataFingerprint != fc.DataFingerprint ||
            !previousResults.TryGetValue(fc.FcIdKey, out previousResult) ||
            !previousResult.IsComplete)
        {
            return false;
        }

        var wasAwaitingTracker = previousProgress.TryGetValue(fc.FcIdKey, out var oldProgress) &&
                                 oldProgress.Status == FcCalculationStatus.AwaitingTrackerUpdate;
        if (oldProgress is not null &&
            oldProgress.Status is not (FcCalculationStatus.Complete or FcCalculationStatus.Reused or FcCalculationStatus.AwaitingTrackerUpdate))
        {
            previousResult = null;
            return false;
        }

        awaitingTracker = wasAwaitingTracker || fc.Submarines.Any(submarine =>
            submarine.Rank < targetRank &&
            submarine.CurrentRoute.Count > 0 &&
            submarine.ReturnAtUtc > previousSnapshot.GeneratedAtUtc &&
            submarine.ReturnAtUtc <= now);
        if (awaitingTracker)
            return true;

        var wasAlreadyIdle = previousState.Submarines.Any(submarine =>
            submarine.Rank < targetRank && submarine.ReturnAtUtc <= previousSnapshot.GeneratedAtUtc);
        if (wasAlreadyIdle)
        {
            previousResult = null;
            return false;
        }

        return true;
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

public enum ForecastRefreshMode
{
    Incremental,
    Full,
}

public enum FcCalculationStatus
{
    Queued,
    Calculating,
    Reused,
    AwaitingTrackerUpdate,
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

    public CalculationSettingsFingerprint CalculationSettingsFingerprint { get; init; }

    public IReadOnlyDictionary<string, CalculationSettingsFingerprint> FcCalculationSettingsFingerprints { get; init; }
        = new Dictionary<string, CalculationSettingsFingerprint>(StringComparer.OrdinalIgnoreCase);

    public ForecastRefreshMode RefreshMode { get; init; } = ForecastRefreshMode.Full;
}
