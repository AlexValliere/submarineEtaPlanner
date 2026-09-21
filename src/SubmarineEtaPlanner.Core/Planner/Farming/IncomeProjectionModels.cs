namespace SubmarineEtaPlanner.Planner;

public enum IncomeDisplayMode { History, Projection }
public enum IncomeProjectionHorizon { Days30 = 30, Days90 = 90, Days365 = 365 }
public enum IncomeProjectionSampleSource { Own, Pooled }
public enum IncomeProjectionMatchKind { ExactStats, PreviousRank }

public readonly record struct IncomeProjectionStats(int Surveillance, int Retrieval, int Favor)
{
    public static IncomeProjectionStats From(SubmarineBuild build)
        => new(build.Surveillance, build.Retrieval, build.Favor);
}

public static class IncomeProjectionPreferences
{
    public static IncomeDisplayMode Normalize(IncomeDisplayMode value)
        => Enum.IsDefined(value) ? value : IncomeDisplayMode.History;

    public static IncomeProjectionHorizon Normalize(IncomeProjectionHorizon value)
        => Enum.IsDefined(value) ? value : IncomeProjectionHorizon.Days365;
}

public sealed record IncomeProjectionSample(
    IncomeProjectionSampleSource Source,
    int ReturnCount,
    int ContributingFcCount,
    DateTimeOffset? FirstReturnAtUtc,
    DateTimeOffset? LastReturnAtUtc,
    IncomeProjectionMatchKind MatchKind = IncomeProjectionMatchKind.ExactStats,
    int? ReferenceRank = null,
    IncomeProjectionStats? MatchedStats = null);

public sealed record IncomeSubmarineProjection(
    long SubmarineId,
    string Name,
    int Rank,
    CurrentBuildPresentation Build,
    IReadOnlyList<uint> Route,
    FarmingRouteSource RouteSource,
    TimeSpan? VoyageDuration,
    TimeSpan? CollectionDelay,
    TimeSpan? FullCycleDuration,
    decimal? AverageGilPerVoyage,
    decimal? GilPerDay,
    IncomeProjectionSample Sample,
    string? UnavailableReason)
{
    public IncomeProjectionStats? CurrentStats { get; init; }
    public int ExactMatchingReturnCount { get; init; }
    // Null means the fallback is disabled or its reference build/route cannot be resolved.
    public int? PreviousRankMatchingReturnCount { get; init; }
    public bool IsAvailable => GilPerDay.HasValue;
    public bool IsApproximate => IsAvailable && Sample.MatchKind == IncomeProjectionMatchKind.PreviousRank;
    public decimal? ProjectedGil(IncomeProjectionHorizon horizon)
        => GilPerDay * (int)IncomeProjectionPreferences.Normalize(horizon);
}

public sealed record IncomeProjectionTotals(decimal? GilPerDay, int EstimatedSubmarines, int FarmingSubmarines,
    int ApproximateSubmarines = 0)
{
    public int ExactSubmarines => EstimatedSubmarines - ApproximateSubmarines;
    public bool IncludesApproximations => ApproximateSubmarines > 0;
    public bool IsPartial => EstimatedSubmarines > 0 && EstimatedSubmarines < FarmingSubmarines;
    public decimal? ProjectedGil(IncomeProjectionHorizon horizon)
        => GilPerDay * (int)IncomeProjectionPreferences.Normalize(horizon);
    public string Coverage => $"{EstimatedSubmarines} of {FarmingSubmarines} farming submarines estimated";
    public string MatchCoverage => $"{ExactSubmarines} exact · {ApproximateSubmarines} approximate";

    public static IncomeProjectionTotals From(IEnumerable<IncomeSubmarineProjection> submarines)
    {
        var all = submarines.ToArray();
        var available = all.Where(submarine => submarine.IsAvailable).ToArray();
        return new(available.Length == 0 ? null : available.Sum(submarine => submarine.GilPerDay!.Value),
            available.Length, all.Length, available.Count(submarine => submarine.IsApproximate));
    }
}

public sealed record IncomeFcProjection(
    string FcIdKey,
    string FreeCompanyTag,
    string World,
    IReadOnlyList<IncomeSubmarineProjection> Submarines,
    IncomeProjectionTotals Totals)
{
    public string DisplayName => string.IsNullOrWhiteSpace(World) ? FreeCompanyTag : $"{FreeCompanyTag} - {World}";
}

public sealed record IncomeProjectionResult(
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset NextRefreshAtUtc,
    IReadOnlyList<IncomeFcProjection> FreeCompanies,
    IReadOnlyList<string> HistoryNotices);

public static class IncomeProjectionPresentation
{
    public static IReadOnlyList<IncomeFcProjection> Select(
        IncomeProjectionResult result, string? fcScope, Func<string, bool> isFavorite)
        => Array.AsReadOnly(result.FreeCompanies
            .Where(fc => fcScope is null ? fc.Totals.FarmingSubmarines > 0
                : string.Equals(fc.FcIdKey, fcScope, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(fc => isFavorite(fc.FcIdKey))
            .ThenByDescending(fc => fc.Totals.GilPerDay.HasValue)
            .ThenByDescending(fc => fc.Totals.GilPerDay)
            .ThenBy(fc => fc.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray());

    public static IncomeProjectionTotals Summarize(IEnumerable<IncomeFcProjection> freeCompanies)
        => IncomeProjectionTotals.From(freeCompanies.SelectMany(fc => fc.Submarines));
}
