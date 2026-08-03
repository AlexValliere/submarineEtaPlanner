namespace SubmarineEtaPlanner.Planner;

public enum ExpMode
{
    Guaranteed,
    Average,
}

public enum SimulationMode
{
    Fleet,
    OptimisticPerSub,
}

public enum UnknownCurrentVoyagePolicy
{
    WarnAndIgnore,
    BlockSimulation,
    ManualOverride,
}

public enum RouteGoal
{
    FastestLevelingOnly,
    UnlockSubSlotsThenLevel,
    UnlockEverythingThenLevel,
    UnlockLevelingRoutesThenLevel,
}

public enum EtaModel
{
    PracticalLeveling,
    ExactRouteSearch,
}

public enum CalculationStatus
{
    Complete,
    Partial,
    Failed,
}

public enum UnlockMilestoneKind
{
    SectorUnlocked,
    SectorExplored,
    SubmarineSlotUnlocked,
    MapUnlocked,
}

public enum FcResultFilter
{
    Leveling,
    All,
    Ready,
}

public enum TimeoutResultBehavior
{
    KeepLastComplete,
    ShowPartial,
}

public sealed record FcState(
    byte[] FcId,
    string FreeCompanyTag,
    string World,
    IReadOnlySet<uint> UnlockedPoints,
    IReadOnlySet<uint> ExploredPoints,
    IReadOnlyList<SubmarineState> Submarines)
{
    public string DisplayName => string.IsNullOrWhiteSpace(World) ? FreeCompanyTag : $"{FreeCompanyTag} - {World}";

    public string FcIdKey => Convert.ToHexString(FcId);

    public bool UnlockDataKnown { get; init; } = true;

    public FcDataFingerprint DataFingerprint { get; init; }
}

public sealed record SubmarineState(
    byte[] FcId,
    long SubmarineId,
    string Name,
    int Rank,
    uint CurrentExp,
    uint NextLevelExp,
    SubmarineBuildParts BuildParts,
    DateTimeOffset ReturnAtUtc,
    IReadOnlyList<uint> CurrentRoute,
    bool CurrentVoyageKnown,
    IReadOnlyList<uint> ManualCurrentRouteOverride)
{
    public bool IsAvailable(DateTimeOffset now) => ReturnAtUtc <= now;
}

public sealed record SubmarineBuildParts(ushort Hull, ushort Stern, ushort Bow, ushort Bridge)
{
    public static SubmarineBuildParts Empty { get; } = new(0, 0, 0, 0);

    public string ToPartCode() => $"{Hull}/{Stern}/{Bow}/{Bridge}";
}

public sealed record UnlockState(
    HashSet<uint> UnlockedPoints,
    HashSet<uint> ExploredPoints,
    HashSet<uint> PendingUnlockPoints,
    List<UnlockMilestone> UnlockMilestones)
{
    public HashSet<uint> PendingExplorePoints { get; init; } = [];

    public Dictionary<uint, int> PendingUnlockAttempts { get; init; } = [];

    public int KnownSubmarineSlots { get; set; } = 1;

    public UnlockState DeepClone() => new(
        new HashSet<uint>(UnlockedPoints),
        new HashSet<uint>(ExploredPoints),
        new HashSet<uint>(PendingUnlockPoints),
        new List<UnlockMilestone>(UnlockMilestones))
    {
        PendingExplorePoints = new HashSet<uint>(PendingExplorePoints),
        PendingUnlockAttempts = new Dictionary<uint, int>(PendingUnlockAttempts),
        KnownSubmarineSlots = KnownSubmarineSlots,
    };
}

public sealed record EtaPercentiles(
    DateTimeOffset P10AtUtc,
    DateTimeOffset P50AtUtc,
    DateTimeOffset P90AtUtc,
    int SampleCount);

public sealed record RouteOutcome(
    IReadOnlyList<uint> Route,
    double Probability,
    IReadOnlyList<uint> RequiredProjectedUnlocks);

public sealed record UnlockAttemptForecast(
    uint SourcePoint,
    uint TargetPoint,
    IReadOnlyList<long> SubmarineIds,
    IReadOnlyList<string> SubmarineNames,
    DateTimeOffset EarliestReturnAtUtc,
    DateTimeOffset LatestReturnAtUtc,
    double CombinedSuccessProbability);

public sealed record UnlockMilestone(
    long SubmarineId,
    uint SourcePoint,
    uint UnlockedPoint,
    DateTimeOffset ReturnAtUtc,
    UnlockMilestoneKind Kind = UnlockMilestoneKind.SectorUnlocked);

public sealed record BuildProfileStep(int MinRank, int MaxRank, string BuildCode)
{
    public bool Contains(int rank) => rank >= MinRank && rank <= MaxRank;
}

public sealed record VoyagePlan(
    long SubmarineId,
    string SubmarineName,
    DateTimeOffset DepartAtUtc,
    DateTimeOffset ReturnAtUtc,
    string BuildCode,
    IReadOnlyList<uint> Route,
    uint ExpGain,
    int RankBefore,
    int RankAfter,
    uint ExpBefore,
    uint ExpAfter,
    IReadOnlyList<uint> UnlocksApplied,
    IReadOnlyList<string> Warnings,
    TimeSpan Duration,
    double ExpPerHour,
    EtaModel EtaModel,
    bool DurationCapApplied,
    int RepeatCount = 1,
    uint ExpPerVoyage = 0,
    TimeSpan PerVoyageDuration = default)
{
    public bool DependsOnProjectedUnlocks { get; init; }

    public IReadOnlyList<uint> RequiredProjectedUnlocks { get; init; } = [];
}

public sealed record PerSubEtaResult(
    long SubmarineId,
    string SubmarineName,
    int StartingRank,
    int FinalRank,
    DateTimeOffset EtaAtUtc,
    TimeSpan Remaining,
    int VoyageCount,
    string PlannedBuild,
    IReadOnlyList<uint> NextRoute,
    IReadOnlyList<VoyagePlan> VoyagePreview,
    IReadOnlyList<UnlockMilestone> UnlockMilestones,
    IReadOnlyList<string> Warnings,
    CalculationStatus Status,
    string? IncompleteReason)
{
    public bool IsComplete => Status == CalculationStatus.Complete;

    public IReadOnlyList<uint> CurrentRoute { get; init; } = [];

    public DateTimeOffset? CurrentReturnAtUtc { get; init; }

    public EtaPercentiles? EtaForecast { get; init; }

    public IReadOnlyList<RouteOutcome> NextRouteOutcomes { get; init; } = [];
}

public sealed record EtaResult(
    byte[] FcId,
    string FcDisplayName,
    DateTimeOffset GeneratedAtUtc,
    int TargetRank,
    SimulationMode Mode,
    IReadOnlyList<PerSubEtaResult> PerSubResults,
    DateTimeOffset FcCompletionAtUtc,
    int VoyageCount,
    IReadOnlyList<VoyagePlan> PlannedRoutes,
    IReadOnlyList<UnlockMilestone> UnlockMilestones,
    IReadOnlyList<string> Warnings,
    CalculationStatus Status,
    string? IncompleteReason)
{
    public bool IsComplete => Status == CalculationStatus.Complete;

    public EtaPercentiles? CompletionForecast { get; init; }

    public IReadOnlyList<UnlockAttemptForecast> ActiveUnlockAttempts { get; init; } = [];

    public int ProbabilitySampleCount { get; init; }
}

public sealed record EtaSettings
{
    public int TargetRank { get; set; } = 114;

    public ExpMode ExpMode { get; set; } = ExpMode.Average;

    public int CollectionDelayMinutes { get; set; } = 0;

    public SimulationMode SimulationMode { get; set; } = SimulationMode.Fleet;

    public List<BuildProfileStep> BuildProfile { get; set; } = [];

    public bool PrioritizeSubSlots { get; set; } = true;

    public RouteGoal RouteGoal { get; set; } = RouteGoal.UnlockLevelingRoutesThenLevel;

    public int DurationLimitHours { get; set; } = 0;

    public EtaModel EtaModel { get; set; } = EtaModel.PracticalLeveling;

    public int PracticalMaxVoyageHours { get; set; } = 0;

    public TimeoutResultBehavior TimeoutResultBehavior { get; set; } = TimeoutResultBehavior.KeepLastComplete;

    public bool ShowRouteDiagnostics { get; set; } = true;

    public bool OptimizeExpPerHour { get; set; } = true;

    public UnknownCurrentVoyagePolicy UnknownCurrentVoyagePolicy { get; set; } = UnknownCurrentVoyagePolicy.WarnAndIgnore;

    public Dictionary<string, List<uint>> ManualCurrentRouteOverrides { get; set; } = [];

    public string? SubmarineTrackerDatabasePathOverride { get; set; }

    public int MaxPreviewVoyagesPerSubmarine { get; set; } = 20;

    public int SimulationSafetyVoyageCapPerSubmarine { get; set; } = 500;

    public int CalculationTimeLimitSeconds { get; set; } = 20;

    public double UnlockSuccessProbability { get; set; } = 0.33;

    public ExpMode GetEffectiveExpMode() => EtaModel == EtaModel.PracticalLeveling ? ExpMode.Average : ExpMode;

    public RouteGoal GetEffectiveRouteGoal() => EtaModel == EtaModel.PracticalLeveling ? RouteGoal.UnlockLevelingRoutesThenLevel : RouteGoal;

    public int GetEffectiveDurationLimitHours() =>
        EtaModel == EtaModel.PracticalLeveling
            ? (DurationLimitHours > 0 ? DurationLimitHours : Math.Max(0, PracticalMaxVoyageHours))
            : DurationLimitHours;

    public bool GetEffectiveOptimizeExpPerHour() => EtaModel == EtaModel.PracticalLeveling || OptimizeExpPerHour;

    public static EtaSettings CreateDefault() => new()
    {
        BuildProfile =
        [
            new BuildProfileStep(1, 14, "SSSS"),
            new BuildProfileStep(15, 24, "SSUS"),
            new BuildProfileStep(25, 999, "SSUW"),
        ],
    };
}

public sealed record CalculationMetrics(
    long ElapsedMilliseconds,
    long RouteQueries,
    long RouteCacheHits,
    long RoutesEvaluated,
    int CalculatedFreeCompanies = 0,
    int ReusedFreeCompanies = 0,
    int AwaitingTrackerFreeCompanies = 0,
    long RouteRankingBuilds = 0,
    long RouteRankingCacheHits = 0,
    long RankedRoutesEvaluated = 0,
    long ExhaustiveRoutesEvaluated = 0,
    long RouteRankingBuildMilliseconds = 0,
    long ExactRouteCacheEvictions = 0,
    long RouteRankingCacheEvictions = 0);

public sealed class ResultsViewState
{
    public bool? ExpansionOverride { get; private set; }

    public void ExpandAll() => ExpansionOverride = true;

    public void CollapseAll() => ExpansionOverride = false;

    public void ClearExpansionOverride() => ExpansionOverride = null;

    public static bool IsReady(EtaResult result, int targetRank)
        => result.PerSubResults.Count > 0 && result.PerSubResults.All(sub => sub.StartingRank >= targetRank);

    public static bool ShouldInclude(EtaResult result, int targetRank, FcResultFilter filter)
        => filter switch
        {
            FcResultFilter.Leveling => !IsReady(result, targetRank),
            FcResultFilter.Ready => IsReady(result, targetRank),
            _ => true,
        };

    public static string SelectCollapsedStatus(
        string resultStatus,
        FcCalculationStatus? calculationStatus,
        string calculationStatusText)
        => calculationStatus is
            FcCalculationStatus.Queued or
            FcCalculationStatus.Calculating or
            FcCalculationStatus.AwaitingTrackerUpdate or
            FcCalculationStatus.TimedOut or
            FcCalculationStatus.Failed
                ? calculationStatusText
                : resultStatus;
}
