namespace SubmarineEtaPlanner.Planner;

public interface ISubmarineCatalog
{
    SubmarineBuild ResolveBuild(string buildCode, int rank);

    SubmarineBuild? ResolveBuild(SubmarineBuildParts buildParts, int rank);

    IReadOnlyList<RouteCandidate> GetCandidateRoutes(
        SubmarineBuild build,
        IReadOnlySet<uint> unlockedPoints,
        IReadOnlySet<uint> exploredPoints,
        IReadOnlySet<uint> mustInclude,
        EtaSettings settings,
        DateTimeOffset? deadlineUtc = null);

    uint CalculateExp(IReadOnlyList<uint> route, SubmarineBuild build, ExpMode expMode);

    TimeSpan CalculateDuration(IReadOnlyList<uint> route, SubmarineBuild build);

    (int Rank, uint CurrentExp, uint NextLevelExp) ApplyExp(int rank, uint currentExp, uint gainedExp, int targetRank);

    string PointName(uint point);

    bool IsPostTargetFarmingReady(SubmarineBuild build, IReadOnlySet<uint> unlockedPoints);

    IReadOnlyList<UnlockRule> UnlockRules { get; }
}

public sealed record SubmarineBuild(string Code, int Rank, int Surveillance, int Retrieval, int Favor, int Range, int Speed);

public sealed record RouteCandidate(
    IReadOnlyList<uint> Route,
    uint Exp,
    TimeSpan Duration,
    double ExpPerHour,
    IReadOnlyList<uint> UnlockTargets,
    EtaModel EtaModel,
    bool DurationCapApplied);

public sealed record UnlockRule(uint SourcePoint, uint UnlocksPoint, int RequiredRank, bool UnlocksSubSlot = false);
