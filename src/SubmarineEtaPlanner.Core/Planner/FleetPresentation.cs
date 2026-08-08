namespace SubmarineEtaPlanner.Planner;

public enum FleetMode
{
    Leveling,
    Farming,
}

public enum IncomeView
{
    AllFleets,
    Leveling,
    Farming,
}

public enum IncomeSort
{
    GrossGil,
    GilPerDay,
    GilPerVoyage,
    FcName,
}

public enum OperationalState
{
    Idle,
    Underway,
    ReadyToCollect,
    Syncing,
}

public enum RoutePurpose
{
    Leveling,
    Unlock,
    Farming,
    Unknown,
}

public sealed record CurrentBuildPresentation(string Code, string? UnavailableReason)
{
    public static CurrentBuildPresentation Unavailable { get; } = new(
        "—",
        "The current build is unavailable because SubmarineTracker did not provide complete build data.");

    public static CurrentBuildPresentation NotResolved { get; } = new(
        "—",
        "Current build data was not resolved for this calculation.");

    public bool IsAvailable => UnavailableReason is null;

    public static CurrentBuildPresentation Create(SubmarineBuild? build)
        => string.IsNullOrWhiteSpace(build?.Code)
            ? Unavailable
            : new CurrentBuildPresentation(build.Code, null);
}

public sealed record SubmarineOperationalProjection(
    long SubmarineId,
    string Name,
    int Rank,
    int EffectiveTargetRank,
    OperationalState State,
    string StateLabel,
    string ActionLabel,
    bool NeedsImmediateAction,
    DateTimeOffset? NextActionAtUtc,
    IReadOnlyList<uint> DisplayedRoute,
    IReadOnlyList<uint> RecommendedNextRoute,
    RoutePurpose RoutePurpose,
    uint? ExpectedExp,
    int? ProjectedRank,
    DateTimeOffset? TargetEtaAtUtc,
    int VoyagesRemaining,
    string? ProjectionUnavailableReason,
    IReadOnlyList<RouteOutcome> AlternativeRoutes)
{
    public CurrentBuildPresentation CurrentBuild { get; init; } = CurrentBuildPresentation.Unavailable;
}

public sealed record FcOperationalProjection(
    FcState State,
    EtaResult? Result,
    int EffectiveTargetRank,
    FleetMode Mode,
    IReadOnlyList<SubmarineOperationalProjection> Submarines,
    DateTimeOffset? CompletionP50AtUtc,
    DateTimeOffset? CompletionP10AtUtc,
    DateTimeOffset? CompletionP90AtUtc)
{
    public int ReadyCount => Submarines.Count(submarine => submarine.Rank >= EffectiveTargetRank);
    public int ImmediateActionCount => Submarines.Count(submarine => submarine.NeedsImmediateAction);
    public DateTimeOffset? EarliestFutureReturnAtUtc => Submarines
        .Where(submarine => submarine.State is OperationalState.Underway or OperationalState.Syncing)
        .Select(submarine => submarine.NextActionAtUtc)
        .Where(value => value is not null)
        .Min();
    public int ActionSortBucket => ImmediateActionCount > 0
        ? 0
        : Submarines.Any(submarine => submarine.State == OperationalState.Syncing) ? 1 : 2;
}

internal sealed record OperationsFcHeaderPresentation(
    string FreeCompany,
    string World,
    string Mode,
    string Attention,
    string FarmReady,
    string Ranks,
    bool HasImmediateActions,
    bool IsFarming)
{
    public static OperationsFcHeaderPresentation Create(
        FcOperationalProjection projection,
        bool favorite,
        DateTimeOffset now)
    {
        var attention = projection.ImmediateActionCount > 0
            ? $"{projection.ImmediateActionCount} action{(projection.ImmediateActionCount == 1 ? string.Empty : "s")} now"
            : projection.EarliestFutureReturnAtUtc is { } next
                ? $"In {CurrentVoyageProgressFormatter.FormatCountdown(next - now)}"
                : "No known return";
        var farmReady = projection.Mode == FleetMode.Farming
            ? "Ready"
            : projection.CompletionP50AtUtc is { } eta
                ? FormatFarmReady(eta - now)
                : "Unavailable";
        return new OperationsFcHeaderPresentation(
            $"{(favorite ? "★ " : string.Empty)}{projection.State.FreeCompanyTag}",
            string.IsNullOrWhiteSpace(projection.State.World) ? "—" : projection.State.World,
            projection.Mode.ToString(),
            attention,
            farmReady,
            projection.Submarines.Count == 0
                ? "—"
                : string.Join(" · ", projection.Submarines.Select(submarine => $"R{submarine.Rank}")),
            projection.ImmediateActionCount > 0,
            projection.Mode == FleetMode.Farming);
    }

    private static string FormatFarmReady(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
            return "Now";
        var rounded = TimeSpan.FromMinutes(Math.Ceiling(remaining.TotalMinutes));
        return rounded.Days > 0
            ? $"{rounded.Days}d {rounded.Hours}h"
            : rounded.Hours > 0
                ? $"{rounded.Hours}h {rounded.Minutes}m"
                : $"{rounded.Minutes}m";
    }
}

internal sealed record CompactOperationalStatePresentation(string Label, string Tooltip)
{
    public static CompactOperationalStatePresentation Create(SubmarineOperationalProjection submarine)
    {
        var label = submarine.State switch
        {
            OperationalState.Syncing => "Syncing",
            OperationalState.ReadyToCollect => "To collect",
            OperationalState.Underway => "Underway",
            _ => "Idle",
        };
        return new CompactOperationalStatePresentation(label, $"{submarine.StateLabel}\n{submarine.ActionLabel}");
    }
}

internal sealed record OperationsRankPresentation(string Label, string? Tooltip)
{
    public static OperationsRankPresentation Create(SubmarineOperationalProjection submarine)
        => submarine.ProjectedRank switch
        {
            null => new($"R{submarine.Rank} → ?", submarine.ProjectionUnavailableReason ?? "Projected rank is unavailable."),
            var rank when rank == submarine.Rank => new($"R{submarine.Rank}", null),
            var rank => new($"R{submarine.Rank} → R{rank}", null),
        };
}

internal sealed record OperationsCompletionPresentation(string Label, string Tooltip)
{
    public static OperationsCompletionPresentation Create(FcOperationalProjection projection)
    {
        if (projection.Mode == FleetMode.Farming)
        {
            return new OperationsCompletionPresentation(
                $"Fleet ready · all {projection.Submarines.Count} submarines are at or above R{projection.EffectiveTargetRank}",
                "Every currently tracked submarine has reached this FC's effective target rank.");
        }

        var prefix = $"Target R{projection.EffectiveTargetRank} · {projection.ReadyCount}/{projection.Submarines.Count} ready";
        if (projection.CompletionP50AtUtc is not { } expected)
            return new OperationsCompletionPresentation($"{prefix} · Expected readiness unavailable", "The forecast did not produce a reliable completion date.");
        var label = $"{prefix} · Expected ready around {expected.LocalDateTime:g}";
        if (projection.CompletionP10AtUtc is { } earliest && projection.CompletionP90AtUtc is { } latest)
            label += $" · Likely between {earliest.LocalDateTime:g} and {latest.LocalDateTime:g}";
        return new OperationsCompletionPresentation(
            label,
            "Based on simulated voyage and unlock outcomes; most simulated results completed within the displayed range.");
    }
}

public static class FleetPresentationFiltering
{
    public static bool Includes(FcOperationalProjection projection, FleetMode? requiredMode)
        => requiredMode is null || projection.Mode == requiredMode.Value;
}

public static class IncomeViewPreferences
{
    public const IncomeView Default = IncomeView.Farming;

    public static IncomeView Normalize(IncomeView value)
        => Enum.IsDefined(value) ? value : Default;

    public static FleetMode? RequiredMode(IncomeView value)
        => Normalize(value) switch
        {
            IncomeView.Leveling => FleetMode.Leveling,
            IncomeView.Farming => FleetMode.Farming,
            _ => null,
        };
}

public static class FleetPresentationBuilder
{
    public static FcOperationalProjection Create(
        FcState fc,
        EtaResult? result,
        EtaSettings effectiveSettings,
        ISubmarineCatalog catalog,
        DateTimeOffset now)
    {
        var resultBySubmarine = result?.PerSubResults.ToDictionary(item => item.SubmarineId) ?? [];
        var projections = fc.Submarines
            .Select(submarine => CreateSubmarine(
                submarine,
                resultBySubmarine.GetValueOrDefault(submarine.SubmarineId),
                effectiveSettings,
                catalog,
                now))
            .OrderBy(submarine => submarine.NeedsImmediateAction ? 0 : 1)
            .ThenBy(submarine => submarine.NextActionAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(submarine => submarine.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var farming = fc.Submarines.Count > 0 && fc.Submarines.All(submarine => submarine.Rank >= effectiveSettings.TargetRank);
        var forecast = result?.CompletionForecast;
        return new FcOperationalProjection(
            fc,
            result,
            effectiveSettings.TargetRank,
            farming ? FleetMode.Farming : FleetMode.Leveling,
            projections,
            forecast?.P50AtUtc ?? result?.FcCompletionAtUtc,
            forecast?.P10AtUtc,
            forecast?.P90AtUtc);
    }

    private static SubmarineOperationalProjection CreateSubmarine(
        SubmarineState submarine,
        PerSubEtaResult? result,
        EtaSettings settings,
        ISubmarineCatalog catalog,
        DateTimeOffset now)
    {
        var progress = CurrentVoyageProgressFormatter.Create(submarine, catalog, now);
        var trackedBuild = catalog.ResolveBuild(submarine.BuildParts, submarine.Rank);
        var currentBuild = CurrentBuildPresentation.Create(trackedBuild);
        var state = !submarine.CurrentVoyageKnown && submarine.ReturnAtUtc != DateTimeOffset.MinValue
            ? OperationalState.Syncing
            : progress.State switch
        {
            CurrentVoyageProgressState.Underway => OperationalState.Underway,
            CurrentVoyageProgressState.ReadyToCollect => OperationalState.ReadyToCollect,
            CurrentVoyageProgressState.Syncing => OperationalState.Syncing,
            _ => OperationalState.Idle,
        };
        var ready = submarine.Rank >= settings.TargetRank;
        var route = ready
            ? state is OperationalState.Underway or OperationalState.ReadyToCollect ? submarine.CurrentRoute : []
            : state is OperationalState.Underway or OperationalState.ReadyToCollect
                ? submarine.CurrentRoute
                : result?.NextRoute ?? [];
        var plannedVoyage = result?.VoyagePreview.FirstOrDefault(plan => plan.Route.SequenceEqual(route)) ??
                            result?.VoyagePreview.FirstOrDefault();
        var purpose = ready
            ? RoutePurpose.Farming
            : plannedVoyage?.UnlockObjective is not null ||
              plannedVoyage is { UnlocksApplied.Count: > 0 } ||
              plannedVoyage?.DependsOnProjectedUnlocks == true
                ? RoutePurpose.Unlock
                : route.Count > 0 ? RoutePurpose.Leveling : RoutePurpose.Unknown;

        uint? expectedExp = null;
        int? projectedRank = null;
        string? unavailableReason = null;
        if (route.Count == 0)
        {
            unavailableReason = ready
                ? "No previous or current farming route is available."
                : "A route is not available until the forecast completes.";
        }
        else if (plannedVoyage is not null && !ready && state == OperationalState.Idle)
        {
            expectedExp = plannedVoyage.ExpGain;
            projectedRank = plannedVoyage.RankAfter;
        }
        else
        {
            if (trackedBuild is null)
            {
                unavailableReason = "The recorded submarine build is incomplete.";
            }
            else
            {
                expectedExp = catalog.CalculateExp(route, trackedBuild, settings.GetEffectiveExpMode());
                projectedRank = catalog.ApplyExp(
                    submarine.Rank,
                    submarine.CurrentExp,
                    expectedExp.Value,
                    catalog.MaximumRank).Rank;
            }
        }

        var action = SelectAction(state, ready, route.Count > 0);
        var stateLabel = state switch
        {
            OperationalState.ReadyToCollect => "Ready to collect",
            OperationalState.Underway => $"Returns {CurrentVoyageProgressFormatter.FormatCountdown(submarine.ReturnAtUtc - now)}",
            OperationalState.Syncing => "Waiting for SubmarineTracker sync",
            _ => "Idle",
        };
        var immediate = state == OperationalState.ReadyToCollect || state == OperationalState.Idle;
        DateTimeOffset? nextAction = immediate
            ? now
            : submarine.ReturnAtUtc == DateTimeOffset.MinValue ? null : submarine.ReturnAtUtc;
        return new SubmarineOperationalProjection(
            submarine.SubmarineId,
            submarine.Name,
            submarine.Rank,
            settings.TargetRank,
            state,
            stateLabel,
            action,
            immediate,
            nextAction,
            route,
            ready && route.Count > 0 ? route : result?.NextRoute ?? [],
            purpose,
            expectedExp,
            projectedRank,
            ready ? now : result?.EtaAtUtc,
            ready ? 0 : result?.VoyageCount ?? 0,
            unavailableReason,
            result?.NextRouteOutcomes ?? [])
        {
            CurrentBuild = currentBuild,
        };
    }

    private static string SelectAction(OperationalState state, bool ready, bool hasKnownRoute)
    {
        if (state == OperationalState.Syncing)
            return "Wait for SubmarineTracker synchronization";
        if (ready)
        {
            if (!hasKnownRoute)
                return "Choose farming route";
            return state == OperationalState.ReadyToCollect
                ? "Collect and resend farming route now"
                : state == OperationalState.Idle
                    ? "Send farming route now"
                    : "Resend farming route after collection";
        }

        return state switch
        {
            OperationalState.ReadyToCollect => "Collect now; send the modeled route after synchronization",
            OperationalState.Idle => "Send recommended leveling route now",
            _ => "Send recommended leveling route after collection",
        };
    }
}

public static class FleetPresentationOrdering
{
    public static IReadOnlyList<FcOperationalProjection> ActionsFirst(
        IEnumerable<FcOperationalProjection> projections,
        Func<FcOperationalProjection, bool> isFavorite)
        => projections
            .OrderByDescending(isFavorite)
            .ThenBy(projection => projection.ActionSortBucket)
            .ThenBy(projection => projection.Submarines
                .Select(submarine => submarine.NextActionAtUtc)
                .Where(value => value is not null)
                .Min() ?? DateTimeOffset.MaxValue)
            .ThenBy(projection => projection.State.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<FcOperationalProjection> FarmReadyEta(
        IEnumerable<FcOperationalProjection> projections,
        Func<FcOperationalProjection, bool> isFavorite)
        => projections
            .OrderByDescending(isFavorite)
            .ThenBy(projection => projection.Mode == FleetMode.Farming ? 0 : 1)
            .ThenBy(projection => projection.Mode == FleetMode.Farming
                ? DateTimeOffset.MinValue
                : projection.CompletionP50AtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(projection => projection.State.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<FcOperationalProjection> ByName(
        IEnumerable<FcOperationalProjection> projections,
        Func<FcOperationalProjection, bool> isFavorite)
        => projections
            .OrderByDescending(isFavorite)
            .ThenBy(projection => projection.State.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

internal sealed record VoyageRoutePurposePresentation(string Label, string Tooltip)
{
    public static VoyageRoutePurposePresentation Create(VoyagePlan plan, Func<uint, string> pointName)
    {
        if (plan.UnlockObjective is not { } objective)
        {
            return new VoyageRoutePurposePresentation(
                "Best available EXP/hour",
                "No intentional unlock objective was selected; this was the best available leveling route.");
        }

        var required = pointName(objective.RequiredPoint);
        var target = pointName(objective.TargetPoint);
        return objective.Kind switch
        {
            UnlockObjectiveKind.ExploreSubmarineSlot => new VoyageRoutePurposePresentation(
                "Unlock submarine slot",
                $"Explore {target} to unlock the next submarine slot."),
            UnlockObjectiveKind.MainProgression => new VoyageRoutePurposePresentation(
                "Continue map progression",
                $"Visit {required} to unlock the next progression destination, {target}."),
            _ => new VoyageRoutePurposePresentation(
                $"Unlock {target}",
                $"Visit {required} to unlock the next destination, {target}."),
        };
    }
}

public sealed record IncomeSubmarineMetrics(
    long SubmarineId,
    string Name,
    long GrossGil,
    int ValidVoyages,
    double GilPerDay,
    double GilPerVoyage,
    DateTimeOffset? FirstReturnAtUtc,
    DateTimeOffset? LastReturnAtUtc)
{
    public int Rank { get; init; }
    public CurrentBuildPresentation CurrentBuild { get; init; } = CurrentBuildPresentation.Unavailable;
}

public sealed record IncomeFcMetrics(
    string FcIdKey,
    string FcDisplayName,
    long GrossGil,
    int ValidVoyages,
    double GilPerDay,
    double GilPerVoyage,
    double CoveredDays,
    DateTimeOffset? FirstReturnAtUtc,
    DateTimeOffset? LastReturnAtUtc,
    IReadOnlyList<IncomeSubmarineMetrics> Submarines);

public sealed record IncomeSummaryMetrics(
    long GrossGil,
    int VoyageCount,
    double CoveredDays,
    double GilPerDay,
    double GilPerVoyage,
    int FcCount);

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

internal sealed record IncomeFcHeaderPresentation(
    string WidgetId,
    string FreeCompany,
    string World,
    string Mode,
    string GrossGil,
    string GilPerDay,
    string GilPerVoyage,
    string Voyages,
    bool IsFarming)
{
    public string BuildsAndRanks { get; init; } = "—";

    public static IncomeFcHeaderPresentation Create(
        FcOperationalProjection projection,
        IncomeFcMetrics metric,
        bool favorite)
        => new(
            $"income-{metric.FcIdKey}",
            $"{(favorite ? "★ " : string.Empty)}{projection.State.FreeCompanyTag}",
            string.IsNullOrWhiteSpace(projection.State.World) ? "—" : projection.State.World,
            projection.Mode.ToString(),
            $"{metric.GrossGil:N0}",
            $"{metric.GilPerDay:N0}",
            $"{metric.GilPerVoyage:N0}",
            metric.ValidVoyages.ToString("N0"),
            projection.Mode == FleetMode.Farming)
        {
            BuildsAndRanks = metric.Submarines.Count == 0
                ? "—"
                : $"[{string.Join(" | ", metric.Submarines.Select(submarine => $"{submarine.CurrentBuild.Code}:{submarine.Rank}"))}]",
        };
}

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
