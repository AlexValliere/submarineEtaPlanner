namespace SubmarineEtaPlanner.Planner;

public enum FleetMode
{
    Leveling,
    Farming,
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
    IReadOnlyList<RouteOutcome> AlternativeRoutes);

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
            : plannedVoyage is { UnlocksApplied.Count: > 0 } || plannedVoyage?.DependsOnProjectedUnlocks == true
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
            var build = catalog.ResolveBuild(submarine.BuildParts, submarine.Rank);
            if (build is null)
            {
                unavailableReason = "The recorded submarine build is incomplete.";
            }
            else
            {
                expectedExp = catalog.CalculateExp(route, build, settings.GetEffectiveExpMode());
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
            result?.NextRouteOutcomes ?? []);
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
}

public sealed record IncomeSubmarineMetrics(
    long SubmarineId,
    string Name,
    long GrossGil,
    int ValidVoyages,
    double GilPerDay,
    double GilPerVoyage,
    DateTimeOffset? FirstReturnAtUtc,
    DateTimeOffset? LastReturnAtUtc);

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

public static class IncomeMetricsCalculator
{
    public static IncomeFcMetrics Calculate(FcState fc, DateTimeOffset now, TimeSpan? period)
    {
        var windowStart = period is null ? (DateTimeOffset?)null : now - period.Value;
        var submarines = fc.Submarines.Select(submarine =>
        {
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
                last);
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
}
