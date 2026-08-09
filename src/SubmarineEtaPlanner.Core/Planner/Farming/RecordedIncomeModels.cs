namespace SubmarineEtaPlanner.Planner;

public enum IncomeView
{
    AllFleets,
    Leveling,
    Farming,
}

public enum IncomeSort
{
    GrossGil = 0,
    ObservedRunRateGilPerDay = 1,
    GilPerVoyage = 2,
    FcName = 3,
    RecordedAverageGilPerDay = 4,

    [Obsolete("Use ObservedRunRateGilPerDay or RecordedAverageGilPerDay explicitly.")]
    GilPerDay = ObservedRunRateGilPerDay,
}

public sealed record IncomeSubmarineMetrics(
    long SubmarineId,
    string Name,
    long GrossGil,
    int VoyageCount,
    double GilPerVoyage,
    double CoveredDays,
    double RecordedAverageGilPerDay,
    double ObservedRunRateGilPerDay,
    DateTimeOffset? FirstReturnAtUtc,
    DateTimeOffset? LastReturnAtUtc)
{
    public int ValidVoyages => VoyageCount;
    public double GilPerDay => ObservedRunRateGilPerDay;
    public int Rank { get; init; }
    public CurrentBuildPresentation CurrentBuild { get; init; } = CurrentBuildPresentation.Unavailable;
    public IReadOnlyList<RecordedVoyageMetrics> RecordedVoyages { get; init; } = [];
    public int KnownFuelVoyageCount { get; init; }
    public int UnknownFuelVoyageCount { get; init; }
    public int TotalRecordedTanks { get; init; }
    public double? AverageTanksPerVoyage { get; init; }
    public double? GrossGilPerTank { get; init; }
    public IReadOnlyDictionary<SectorSetSignature, long> GrossGilByRouteSignature { get; init; }
        = new System.Collections.ObjectModel.ReadOnlyDictionary<SectorSetSignature, long>(
            new Dictionary<SectorSetSignature, long>());
}

public sealed record IncomeFcMetrics(
    string FcIdKey,
    string FcDisplayName,
    long GrossGil,
    int VoyageCount,
    double GilPerVoyage,
    double CoveredDays,
    double RecordedAverageGilPerDay,
    double ObservedRunRateGilPerDay,
    DateTimeOffset? FirstReturnAtUtc,
    DateTimeOffset? LastReturnAtUtc,
    IReadOnlyList<IncomeSubmarineMetrics> Submarines)
{
    public int ValidVoyages => VoyageCount;
    public double GilPerDay => ObservedRunRateGilPerDay;
    public int KnownFuelVoyageCount { get; init; }
    public int UnknownFuelVoyageCount { get; init; }
    public int TotalRecordedTanks { get; init; }
    public double? AverageTanksPerVoyage { get; init; }
    public double? GrossGilPerTank { get; init; }
    public IReadOnlyDictionary<SectorSetSignature, long> GrossGilByRouteSignature { get; init; }
        = new System.Collections.ObjectModel.ReadOnlyDictionary<SectorSetSignature, long>(
            new Dictionary<SectorSetSignature, long>());
}

public sealed record IncomeSummaryMetrics(
    long GrossGil,
    int VoyageCount,
    double GilPerVoyage,
    double CoveredDays,
    double RecordedAverageGilPerDay,
    double ObservedRunRateGilPerDay,
    int FcCount)
{
    public double GilPerDay => ObservedRunRateGilPerDay;
}
