using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using SubmarineEtaPlanner.Planner;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

public sealed partial class PlannerWindow
{
    private EtaPlannerSnapshot? EnsureFleetSnapshot()
    {
        if (this.snapshot is null && this.refreshTask is null)
            StartRefresh();
        if (this.snapshot is null)
        {
            PlannerUi.Callout("loading-fleet-data", FontAwesomeIcon.SyncAlt, "Loading fleet data", "Reading SubmarineTracker and calculating fleet forecasts…", PlannerUi.Cyan);
            return null;
        }
        CheckForTrackerDataChanges();
        return this.snapshot;
    }

    private void DrawFleetNotices(EtaPlannerSnapshot currentSnapshot)
    {
        if (this.trackerDataChanged && this.refreshTask is not { IsCompleted: false })
        {
            PlannerUi.Callout("tracker-change", FontAwesomeIcon.Database, "New tracker data is available", "Refresh to synchronize ranks, returns, routes, and unlock state.", PlannerUi.Amber);
            ImGui.Spacing();
        }
        if (!currentSnapshot.IsRunning && !currentSnapshot.IsComplete)
        {
            PlannerUi.Callout("forecast-warning", FontAwesomeIcon.ExclamationTriangle, "Forecast warning", currentSnapshot.IncompleteReason ?? "One or more FC forecasts are incomplete.", PlannerUi.Amber);
            ImGui.Spacing();
        }
    }

    private IReadOnlyList<FcOperationalProjection> CreateProjections(EtaPlannerSnapshot currentSnapshot, DateTimeOffset now)
    {
        var results = currentSnapshot.Results.ToDictionary(result => Convert.ToHexString(result.FcId));
        return currentSnapshot.FreeCompanies.Select(fc =>
        {
            var preferences = this.configuration.GetFcPreferences(fc.FcIdKey);
            var effective = EffectiveEtaSettingsResolver.Resolve(
                this.configuration.Settings,
                new FcSimulationOverride(preferences.TargetRankOverride, preferences.StrategyOverride),
                this.catalog.MaximumRank);
            var assignments = preferences.Submarines.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Assignment);
            return FleetPresentationBuilder.Create(
                fc,
                results.GetValueOrDefault(fc.FcIdKey),
                effective,
                this.catalog,
                now,
                assignments);
        }).ToArray();
    }

    private void DrawSearch(string hint)
    {
        ImGui.SetNextItemWidth(Math.Min(290f * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X * 0.38f));
        ImGui.InputTextWithHint("##fleet-search", hint, ref this.fcSearch, 100);
    }

    private bool MatchesSearch(FcState fc) => string.IsNullOrWhiteSpace(this.fcSearch) ||
        fc.DisplayName.Contains(this.fcSearch, StringComparison.OrdinalIgnoreCase) ||
        fc.Submarines.Any(submarine => submarine.Name.Contains(this.fcSearch, StringComparison.OrdinalIgnoreCase));

    private bool IsFavorite(FcOperationalProjection projection)
        => this.configuration.GetFcPreferences(projection.State.FcIdKey).Favorite;

    private static void DrawTableText(string text)
    {
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(text);
    }

    private static void DrawCurrentBuild(CurrentBuildPresentation build)
    {
        ImGui.TextUnformatted(build.Code);
        if (build.UnavailableReason is not null)
            PlannerUi.Tooltip(build.UnavailableReason);
    }

    private static string FormatIncomeDate(DateTimeOffset? value)
        => value is null ? "—" : value.Value.LocalDateTime.ToString("g");
}
