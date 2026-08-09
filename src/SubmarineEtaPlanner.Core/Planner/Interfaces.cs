namespace SubmarineEtaPlanner.Planner;

public interface ISubmarineCatalog
{
    int MaximumRank { get; }

    SubmarineBuild ResolveBuild(string buildCode, int rank);

    SubmarineBuild? ResolveBuild(SubmarineBuildParts buildParts, int rank);

    RouteSearchResult FindBestRoute(RouteSearchRequest request);

    uint CalculateExp(IReadOnlyList<uint> route, SubmarineBuild build, ExpMode expMode);

    TimeSpan CalculateDuration(IReadOnlyList<uint> route, SubmarineBuild build);

    (int Rank, uint CurrentExp, uint NextLevelExp) ApplyExp(int rank, uint currentExp, uint gainedExp, int targetRank);

    string PointName(uint point);

    int GetPointRequiredRank(uint point);

    IReadOnlyList<UnlockRule> UnlockRules { get; }
}

public sealed record RouteFuelProfile(
    int CeruleumTanks,
    bool IsComplete,
    IReadOnlyList<uint> UnknownSectors);

public sealed record OrderedRouteOperationalProfile(
    IReadOnlyList<uint> Route,
    RouteFuelProfile Fuel,
    TimeSpan Duration);

public interface IRouteOperationalCatalog
{
    RouteFuelProfile CalculateFuel(
        IReadOnlyCollection<uint> sectors);

    OrderedRouteOperationalProfile AnalyzeOrderedRoute(
        IReadOnlyList<uint> route,
        SubmarineBuild build);
}

public interface IRouteSearchDiagnostics
{
    void ResetRouteSearchMetrics();

    RouteSearchMetrics GetRouteSearchMetrics();
}

public interface IPlannerDataDiagnostics
{
    IReadOnlyList<string> GetPlannerDataWarnings();
}

public interface IEtaSimulator
{
    EtaResult Simulate(
        FcState fc,
        EtaSettings settings,
        DateTimeOffset now,
        DateTimeOffset? deadlineUtc,
        CancellationToken cancellationToken);

    EtaResult Simulate(
        FcState fc,
        EtaSettings settings,
        EtaSimulationScope scope,
        DateTimeOffset now,
        DateTimeOffset? deadlineUtc,
        CancellationToken cancellationToken)
        => Simulate(fc, settings, now, deadlineUtc, cancellationToken);
}

public sealed record SubmarineBuild(string Code, int Rank, int Surveillance, int Retrieval, int Favor, int Range, int Speed);

public sealed record RouteCandidate(
    IReadOnlyList<uint> Route,
    uint Exp,
    TimeSpan Duration,
    double ExpPerHour,
    IReadOnlyList<uint> UnlockTargets,
    EtaModel EtaModel,
    bool DurationCapApplied,
    bool AdvancesUnlockObjective = false)
{
    public UnlockObjective? UnlockObjective { get; init; }
}

public sealed record UnlockRule(
    uint SourcePoint,
    uint UnlocksPoint,
    int SourceRequiredRank,
    int TargetRequiredRank,
    bool UnlocksSubSlot = false,
    bool UnlocksMap = false,
    bool IsMainProgression = false)
{
    public UnlockRule(uint sourcePoint, uint unlocksPoint, int requiredRank, bool UnlocksSubSlot = false)
        : this(sourcePoint, unlocksPoint, requiredRank, requiredRank, UnlocksSubSlot, false, false)
    {
    }
}

public enum UnlockObjectiveKind
{
    SectorUnlock,
    ExploreSubmarineSlot,
    MainProgression,
}

public sealed record UnlockObjective(uint RequiredPoint, uint TargetPoint, UnlockObjectiveKind Kind);

public readonly record struct SectorMask(ulong Low, ulong Middle, ulong High)
{
    public static SectorMask From(IEnumerable<uint> points)
    {
        var mask = new SectorMask();
        foreach (var point in points)
            mask = mask.Add(point);
        return mask;
    }

    public SectorMask Add(uint point) => point switch
    {
        < 64 => this with { Low = Low | (1UL << (int)point) },
        < 128 => this with { Middle = Middle | (1UL << (int)(point - 64)) },
        < 192 => this with { High = High | (1UL << (int)(point - 128)) },
        _ => this,
    };

    public bool Contains(uint point) => point switch
    {
        < 64 => (Low & (1UL << (int)point)) != 0,
        < 128 => (Middle & (1UL << (int)(point - 64))) != 0,
        < 192 => (High & (1UL << (int)(point - 128))) != 0,
        _ => false,
    };

    public bool ContainsAll(SectorMask other)
        => (Low & other.Low) == other.Low &&
           (Middle & other.Middle) == other.Middle &&
           (High & other.High) == other.High;

    public bool Intersects(SectorMask other)
        => (Low & other.Low) != 0 || (Middle & other.Middle) != 0 || (High & other.High) != 0;

    public bool IsEmpty => Low == 0 && Middle == 0 && High == 0;
}

public sealed record RouteSearchRequest(
    SubmarineBuild Build,
    IReadOnlySet<uint> UnlockedPoints,
    SectorMask UnlockedMask,
    SectorMask MustIncludeMask,
    EtaSettings Settings,
    SectorMask ExcludedSectorMask = default,
    DateTimeOffset? DeadlineUtc = null,
    CancellationToken CancellationToken = default);

public sealed record RouteSearchResult(RouteCandidate? Route, int RoutesEvaluated, bool CacheHit);

public sealed record RouteSearchMetrics(
    long Queries,
    long CacheHits,
    long RoutesEvaluated,
    long RankingBuilds = 0,
    long RankingCacheHits = 0,
    long RankedRoutesEvaluated = 0,
    long ExhaustiveRoutesEvaluated = 0,
    long RankingBuildMilliseconds = 0,
    long ExactCacheEvictions = 0,
    long RankingCacheEvictions = 0);
