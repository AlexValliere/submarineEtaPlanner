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
    public UnlockState DeepClone() => new(
        new HashSet<uint>(UnlockedPoints),
        new HashSet<uint>(ExploredPoints),
        new HashSet<uint>(PendingUnlockPoints),
        new List<UnlockMilestone>(UnlockMilestones));
}

public sealed record UnlockMilestone(
    long SubmarineId,
    uint SourcePoint,
    uint UnlockedPoint,
    DateTimeOffset ReturnAtUtc);

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
    bool DurationCapApplied);

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
    bool PostTargetFarmingReady,
    CalculationStatus Status,
    string? IncompleteReason)
{
    public bool IsComplete => Status == CalculationStatus.Complete;
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
}

public sealed record EtaSettings
{
    public int TargetRank { get; set; } = 114;

    public ExpMode ExpMode { get; set; } = ExpMode.Average;

    public int CollectionDelayMinutes { get; set; } = 0;

    public SimulationMode SimulationMode { get; set; } = SimulationMode.Fleet;

    public List<BuildProfileStep> BuildProfile { get; set; } = [];

    public bool PrioritizeSubSlots { get; set; } = true;

    public RouteGoal RouteGoal { get; set; } = RouteGoal.FastestLevelingOnly;

    public int DurationLimitHours { get; set; } = 0;

    public EtaModel EtaModel { get; set; } = EtaModel.PracticalLeveling;

    public int PracticalMaxVoyageHours { get; set; } = 24;

    public TimeoutResultBehavior TimeoutResultBehavior { get; set; } = TimeoutResultBehavior.KeepLastComplete;

    public bool ShowRouteDiagnostics { get; set; } = true;

    public bool OptimizeExpPerHour { get; set; } = true;

    public UnknownCurrentVoyagePolicy UnknownCurrentVoyagePolicy { get; set; } = UnknownCurrentVoyagePolicy.WarnAndIgnore;

    public Dictionary<string, List<uint>> ManualCurrentRouteOverrides { get; set; } = [];

    public bool ShowPost114MrojzReadiness { get; set; } = true;

    public string? SubmarineTrackerDatabasePathOverride { get; set; }

    public int MaxPreviewVoyagesPerSubmarine { get; set; } = 20;

    public int SimulationSafetyVoyageCapPerSubmarine { get; set; } = 500;

    public int CalculationTimeLimitSeconds { get; set; } = 20;

    public ExpMode EffectiveExpMode => EtaModel == EtaModel.PracticalLeveling ? ExpMode.Average : ExpMode;

    public RouteGoal EffectiveRouteGoal => EtaModel == EtaModel.PracticalLeveling ? RouteGoal.FastestLevelingOnly : RouteGoal;

    public int EffectiveDurationLimitHours =>
        EtaModel == EtaModel.PracticalLeveling
            ? (DurationLimitHours > 0 ? DurationLimitHours : Math.Max(1, PracticalMaxVoyageHours))
            : DurationLimitHours;

    public bool EffectiveOptimizeExpPerHour => EtaModel != EtaModel.PracticalLeveling && OptimizeExpPerHour;

    public static EtaSettings CreateDefault() => new()
    {
        BuildProfile =
        [
            new BuildProfileStep(1, 14, "SSSS"),
            new BuildProfileStep(15, 24, "SSUS"),
            new BuildProfileStep(25, 113, "SSUW"),
            new BuildProfileStep(114, 999, "WSCC"),
        ],
    };
}
