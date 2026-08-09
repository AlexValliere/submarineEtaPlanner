namespace SubmarineEtaPlanner.Planner;

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
