namespace SubmarineEtaPlanner.Planner;

public static class IncomeViewPreferences
{
    public const IncomeView Default = IncomeView.Farming;

    public static IncomeView Normalize(IncomeView value)
        => Enum.IsDefined(value) ? value : Default;

    public static FleetMode? RequiredMode(IncomeView value)
        => Normalize(value) switch
        {
            IncomeView.Leveling => FleetMode.Leveling,
            IncomeView.Farming => FleetMode.Farming,
            _ => null,
        };
}

internal sealed record IncomeFcHeaderPresentation(
    string WidgetId,
    string FreeCompany,
    string World,
    string Mode,
    string GrossGil,
    string GilPerDay,
    string GilPerVoyage,
    string Voyages,
    bool IsFarming)
{
    public string BuildsAndRanks { get; init; } = "—";

    public static IncomeFcHeaderPresentation Create(
        FcOperationalProjection projection,
        IncomeFcMetrics metric,
        bool favorite)
        => new(
            $"income-{metric.FcIdKey}",
            $"{(favorite ? "★ " : string.Empty)}{projection.State.FreeCompanyTag}",
            string.IsNullOrWhiteSpace(projection.State.World) ? "—" : projection.State.World,
            FcRoleSummaryFormatter.Format(projection.RoleSummary),
            $"{metric.GrossGil:N0}",
            $"{metric.GilPerDay:N0}",
            $"{metric.GilPerVoyage:N0}",
            metric.ValidVoyages.ToString("N0"),
            projection.RoleSummary is { HasFarming: true, HasLeveling: false, HasPaused: false })
        {
            BuildsAndRanks = metric.Submarines.Count == 0
                ? "—"
                : $"[{string.Join(" | ", metric.Submarines.Select(submarine => $"{submarine.CurrentBuild.Code}:{submarine.Rank}"))}]",
        };
}
