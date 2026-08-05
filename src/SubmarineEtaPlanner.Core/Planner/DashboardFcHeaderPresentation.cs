namespace SubmarineEtaPlanner.Planner;

internal sealed record DashboardFcHeaderPresentation(
    string FreeCompanyTag,
    string World,
    string TargetEta,
    string Salvage,
    string CurrentVoyage)
{
    public static DashboardFcHeaderPresentation Create(
        FcState fc,
        string targetEta,
        FcCurrentVoyageProgressPresentation currentVoyages)
        => new(
            fc.FreeCompanyTag,
            string.IsNullOrWhiteSpace(fc.World) ? "—" : fc.World,
            targetEta,
            $"{ResultsViewState.FormatCompactGil(fc.RecordedSalvageGil)} gil",
            currentVoyages.ReadyCount > 0
                ? currentVoyages.HeaderLabel
                : currentVoyages.Primary is { } primary
                    ? $"In {primary.Countdown}"
                    : "—");
}
