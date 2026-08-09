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
            : new CurrentBuildPresentation(CurrentBuildCodeFormatter.Format(build.Code), null);
}

public sealed record SubmarineOperationalProjection(
    long SubmarineId,
    string Name,
    int Rank,
    int EffectiveTargetRank,
    OperationalState State,
    string StateLabel,
    RecommendedAction Action,
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

    public EffectiveSubmarineRole EffectiveRole { get; init; }

    public bool IsTargetComplete => Rank >= EffectiveTargetRank;
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
    public FcRoleSummary RoleSummary { get; init; } = new(0, 0, 0);

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
