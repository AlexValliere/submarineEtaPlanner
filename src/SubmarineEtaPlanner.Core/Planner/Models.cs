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

public enum FcStrategyPreset
{
    Recommended,
    ImmediateExpOnly,
    SlotsFirstThenImmediateExp,
    UnlockEverythingThenLevel,
}

public sealed record EtaSimulationScope(
    IReadOnlySet<long> TargetSubmarineIds)
{
    public static EtaSimulationScope CreateDefault(FcState fc, int effectiveTargetRank)
        => new(fc.Submarines
            .Where(submarine => submarine.Rank < effectiveTargetRank)
            .Select(submarine => submarine.SubmarineId)
            .ToHashSet());
}

public sealed record FcSimulationOverride(
    int? TargetRank = null,
    FcStrategyPreset? Strategy = null)
{
    private static IReadOnlyDictionary<long, SubmarineAssignment> EmptyAssignments { get; }
        = new System.Collections.ObjectModel.ReadOnlyDictionary<long, SubmarineAssignment>(
            new Dictionary<long, SubmarineAssignment>());

    public IReadOnlyDictionary<long, SubmarineAssignment> SubmarineAssignments { get; init; }
        = EmptyAssignments;

    public static FcSimulationOverride? FromPreferences(FcPreferences preferences)
    {
        var assignments = preferences.Submarines
            .Where(pair => pair.Value.Assignment != SubmarineAssignment.Auto)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Assignment);
        return preferences.TargetRankOverride is null &&
               preferences.StrategyOverride is null &&
               assignments.Count == 0
            ? null
            : new FcSimulationOverride(
                preferences.TargetRankOverride,
                preferences.StrategyOverride)
            {
                SubmarineAssignments = assignments,
            };
    }
}

public sealed record PlannerCalculationRequest(
    EtaSettings GlobalSettings,
    IReadOnlyDictionary<string, FcSimulationOverride> FreeCompanyOverrides)
{
    public static PlannerCalculationRequest FromGlobalSettings(EtaSettings settings)
        => new(settings, new Dictionary<string, FcSimulationOverride>(StringComparer.OrdinalIgnoreCase));
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

    public ulong? GameFreeCompanyId { get; init; }

    public bool UnlockDataKnown { get; init; } = true;

    public FcDataFingerprint DataFingerprint { get; init; }

    public long RecordedSalvageGil => Submarines.Sum(submarine => submarine.Salvage.TotalGil);
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

    public SubmarineSalvageSummary Salvage { get; init; } = SubmarineSalvageSummary.Empty;
}

public sealed record SalvageItemTotal(
    uint ItemId,
    string Name,
    uint NpcSalePrice,
    long Quantity)
{
    public long TotalGil => checked(Quantity * NpcSalePrice);
}

public sealed record SubmarineSalvageSummary(
    int VoyageCount,
    DateTimeOffset? FirstReturnAtUtc,
    DateTimeOffset? LastReturnAtUtc,
    IReadOnlyList<SalvageItemTotal> Items)
{
    public static SubmarineSalvageSummary Empty { get; } = new(0, null, null, []);

    public long ItemCount => Items.Sum(item => item.Quantity);

    public long TotalGil => Items.Sum(item => item.TotalGil);

    public IReadOnlyList<SalvageVoyageRecord> Voyages { get; init; } = [];
}

public sealed record SalvageVoyageRecord(
    string FcIdKey,
    long SubmarineId,
    DateTimeOffset ReturnAtUtc,
    IReadOnlyList<SalvageItemTotal> Items)
{
    public long GrossNpcGil => Items.Sum(item => item.TotalGil);
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

    public UnlockObjective? UnlockObjective { get; init; }
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

    public bool IncludedInLevelingTarget { get; init; } = true;

    public IReadOnlyList<uint> CurrentRoute { get; init; } = [];

    public DateTimeOffset? CurrentReturnAtUtc { get; init; }

    public bool CurrentVoyageUnknown { get; init; }

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
    public int TargetRank { get; set; } = 90;

    public ExpMode ExpMode { get; set; } = ExpMode.Average;

    public int CollectionDelayMinutes { get; set; } = 120;

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

    public EtaSettings DeepClone() => new()
    {
        TargetRank = TargetRank,
        ExpMode = ExpMode,
        CollectionDelayMinutes = CollectionDelayMinutes,
        SimulationMode = SimulationMode,
        BuildProfile = BuildProfile.Select(step => new BuildProfileStep(step.MinRank, step.MaxRank, step.BuildCode)).ToList(),
        PrioritizeSubSlots = PrioritizeSubSlots,
        RouteGoal = RouteGoal,
        DurationLimitHours = DurationLimitHours,
        EtaModel = EtaModel,
        PracticalMaxVoyageHours = PracticalMaxVoyageHours,
        TimeoutResultBehavior = TimeoutResultBehavior,
        ShowRouteDiagnostics = ShowRouteDiagnostics,
        OptimizeExpPerHour = OptimizeExpPerHour,
        UnknownCurrentVoyagePolicy = UnknownCurrentVoyagePolicy,
        ManualCurrentRouteOverrides = ManualCurrentRouteOverrides.ToDictionary(pair => pair.Key, pair => pair.Value.ToList()),
        SubmarineTrackerDatabasePathOverride = SubmarineTrackerDatabasePathOverride,
        MaxPreviewVoyagesPerSubmarine = MaxPreviewVoyagesPerSubmarine,
        SimulationSafetyVoyageCapPerSubmarine = SimulationSafetyVoyageCapPerSubmarine,
        CalculationTimeLimitSeconds = CalculationTimeLimitSeconds,
        UnlockSuccessProbability = UnlockSuccessProbability,
    };
}

public static class EffectiveEtaSettingsResolver
{
    public static EtaSettings Resolve(EtaSettings global, FcSimulationOverride? simulationOverride, int maximumRank)
    {
        var effective = global.DeepClone();
        if (simulationOverride?.TargetRank is { } targetRank)
            effective.TargetRank = Math.Clamp(targetRank, 1, Math.Max(1, maximumRank));

        if (simulationOverride?.Strategy is not { } strategy)
            return effective;

        effective.OptimizeExpPerHour = true;
        switch (strategy)
        {
            case FcStrategyPreset.Recommended:
                effective.EtaModel = EtaModel.PracticalLeveling;
                effective.PrioritizeSubSlots = true;
                effective.RouteGoal = RouteGoal.UnlockLevelingRoutesThenLevel;
                break;
            case FcStrategyPreset.ImmediateExpOnly:
                effective.EtaModel = EtaModel.ExactRouteSearch;
                effective.PrioritizeSubSlots = false;
                effective.RouteGoal = RouteGoal.FastestLevelingOnly;
                break;
            case FcStrategyPreset.SlotsFirstThenImmediateExp:
                effective.EtaModel = EtaModel.ExactRouteSearch;
                effective.PrioritizeSubSlots = true;
                effective.RouteGoal = RouteGoal.UnlockSubSlotsThenLevel;
                break;
            case FcStrategyPreset.UnlockEverythingThenLevel:
                effective.EtaModel = EtaModel.ExactRouteSearch;
                effective.PrioritizeSubSlots = true;
                effective.RouteGoal = RouteGoal.UnlockEverythingThenLevel;
                break;
        }

        return effective;
    }
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

    public static bool IsReady(FcState fc, int targetRank)
        => fc.Submarines.Count > 0 && fc.Submarines.All(submarine => submarine.Rank >= targetRank);

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

    public static string FormatCollapsedHeaderStatus(string status, long recordedSalvageGil)
        => $"{status} • Salvage {FormatCompactGil(recordedSalvageGil)} gil";

    public static string FormatCompactGil(long gil)
        => gil switch
        {
            >= 1_000_000_000 => $"{gil / 1_000_000_000d:0.##}b",
            >= 1_000_000 => $"{gil / 1_000_000d:0.##}m",
            >= 1_000 => $"{gil / 1_000d:0.##}k",
            _ => gil.ToString("N0"),
        };
}
