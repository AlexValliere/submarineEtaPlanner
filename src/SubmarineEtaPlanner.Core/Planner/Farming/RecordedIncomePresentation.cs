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

public static class IncomeSortPreferences
{
    private const int LegacyRecordedAverageValue = 4;

    public static IncomeSort Normalize(IncomeSort value)
        => (int)value switch
        {
            (int)IncomeSort.GrossGil => IncomeSort.GrossGil,
            (int)IncomeSort.RecordedAverageGilPerDay => IncomeSort.RecordedAverageGilPerDay,
            (int)IncomeSort.GilPerVoyage => IncomeSort.GilPerVoyage,
            (int)IncomeSort.FcName => IncomeSort.FcName,
            LegacyRecordedAverageValue => IncomeSort.RecordedAverageGilPerDay,
            _ => IncomeSort.GrossGil,
        };
}

internal sealed record IncomeFcHeaderPresentation(
    string WidgetId,
    string FreeCompany,
    string World,
    string Mode,
    string GrossGil,
    string RecordedAverageGilPerDay,
    string GilPerVoyage,
    string Voyages,
    string Submarine1,
    string Submarine2,
    string Submarine3,
    string Submarine4,
    bool IsFarming)
{
    public static IncomeFcHeaderPresentation Create(
        FcOperationalProjection projection,
        IncomeFcMetrics metric,
        bool favorite)
    {
        var submarines = metric.Submarines
            .Take(4)
            .Select(submarine => $"{submarine.CurrentBuild.Code} · R{submarine.Rank}")
            .Concat(Enumerable.Repeat("—", 4))
            .Take(4)
            .ToArray();

        return new IncomeFcHeaderPresentation(
            $"income-{metric.FcIdKey}",
            $"{(favorite ? "★ " : string.Empty)}{projection.State.FreeCompanyTag}",
            string.IsNullOrWhiteSpace(projection.State.World) ? "—" : projection.State.World,
            FcRoleSummaryFormatter.Format(projection.RoleSummary),
            $"{metric.GrossGil:N0}",
            $"{metric.RecordedAverageGilPerDay:N0}",
            $"{metric.GilPerVoyage:N0}",
            metric.VoyageCount.ToString("N0"),
            submarines[0],
            submarines[1],
            submarines[2],
            submarines[3],
            projection.RoleSummary is { HasFarming: true, HasLeveling: false, HasPaused: false });
    }
}
