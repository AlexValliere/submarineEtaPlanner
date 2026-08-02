using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using SubmarineEtaPlanner.Planner;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

public sealed partial class PlannerWindow
{
    private void DrawDashboardPage()
    {
        if (this.snapshot is null && this.refreshTask is null)
            StartRefresh();

        var refreshing = this.refreshTask is { IsCompleted: false };
        if (!string.IsNullOrWhiteSpace(this.lastError))
        {
            PlannerUi.Callout("dashboard-error", FontAwesomeIcon.ExclamationTriangle, "Calculation notice", this.lastError, PlannerUi.Red);
            ImGui.Spacing();
        }

        if (refreshing)
        {
            var elapsed = this.refreshStartedAtUtc is null
                ? TimeSpan.Zero
                : DateTimeOffset.UtcNow - this.refreshStartedAtUtc.Value;
            var title = this.snapshot is null ? "Charting the first forecast" : "Refreshing forecast";
            var body = this.snapshot is null
                ? $"Reading SubmarineTracker data and calculating routes {FormatElapsed(elapsed)}"
                : $"Existing results remain available while the new forecast is calculated {FormatElapsed(elapsed)}";
            PlannerUi.Callout("dashboard-loading", FontAwesomeIcon.SyncAlt, title, body, PlannerUi.Cyan);
            ImGui.Spacing();
            if (this.snapshot is null)
                return;
        }

        var currentSnapshot = this.snapshot;
        if (currentSnapshot is null)
            return;

        DrawSummaryCards(currentSnapshot);
        ImGui.Spacing();
        DrawResultsToolbar();
        ImGui.Spacing();

        if (currentSnapshot.Metrics is not null)
        {
            var metrics = currentSnapshot.Metrics;
            ImGui.TextColored(
                PlannerUi.Muted,
                $"Calculated in {metrics.ElapsedMilliseconds:N0} ms  •  {metrics.RouteQueries:N0} route queries  •  " +
                $"{metrics.RouteCacheHits:N0} cache hits  •  {metrics.RoutesEvaluated:N0} routes checked");
        }

        if (currentSnapshot.Warnings.Count > 0 || !currentSnapshot.IsComplete)
        {
            ImGui.Spacing();
            var warningText = new List<string>();
            if (!currentSnapshot.IsComplete && currentSnapshot.IncompleteReason is not null)
                warningText.Add(currentSnapshot.IncompleteReason);
            warningText.AddRange(currentSnapshot.Warnings.Distinct());
            PlannerUi.Callout(
                "snapshot-warnings",
                FontAwesomeIcon.ExclamationTriangle,
                currentSnapshot.IsComplete ? "Forecast warnings" : "Partial forecast",
                string.Join("  •  ", warningText),
                PlannerUi.Amber);
        }

        var visibleResults = currentSnapshot.Results
            .Where(result => ResultsViewState.ShouldInclude(
                result,
                this.configuration.Settings.TargetRank,
                this.configuration.ResultsFilter))
            .Where(result => string.IsNullOrWhiteSpace(this.fcSearch) ||
                             result.FcDisplayName.Contains(this.fcSearch, StringComparison.OrdinalIgnoreCase))
            .OrderBy(result => result.FcDisplayName)
            .ToArray();

        ImGui.Spacing();
        ImGui.TextColored(PlannerUi.Muted, $"{visibleResults.Length} shown of {currentSnapshot.Results.Count} tracked free companies");
        ImGui.Spacing();

        if (currentSnapshot.Results.Count == 0)
        {
            PlannerUi.Callout(
                "empty-data",
                FontAwesomeIcon.Database,
                "No SubmarineTracker data found",
                "Check the Data source page if your tracker database is stored outside its normal location.",
                PlannerUi.Amber);
            return;
        }

        if (visibleResults.Length == 0)
        {
            PlannerUi.Callout(
                "empty-filter",
                FontAwesomeIcon.Search,
                "No matching fleets",
                "Change the search text or choose another readiness filter.",
                PlannerUi.Cyan);
            return;
        }

        foreach (var result in visibleResults)
        {
            DrawFcResult(result);
            ImGui.Spacing();
        }

        this.viewState.ClearExpansionOverride();
    }

    private void DrawSummaryCards(EtaPlannerSnapshot currentSnapshot)
    {
        var total = currentSnapshot.Results.Count;
        var leveling = currentSnapshot.Results.Count(result =>
            !ResultsViewState.IsReady(result, this.configuration.Settings.TargetRank));
        var ready = total - leveling;
        var warnings = currentSnapshot.Results.Count(result =>
            !result.IsComplete || result.Warnings.Count > 0 || result.PerSubResults.Any(sub => sub.Warnings.Count > 0));

        if (!ImGui.BeginTable("summary-cards", 4, ImGuiTableFlags.SizingStretchSame))
            return;

        ImGui.TableNextColumn();
        PlannerUi.MetricCard("metric-tracked", FontAwesomeIcon.Ship, total.ToString(), "Tracked FCs", PlannerUi.Cyan);
        ImGui.TableNextColumn();
        PlannerUi.MetricCard("metric-leveling", FontAwesomeIcon.ChartLine, leveling.ToString(), "Leveling", PlannerUi.Teal);
        ImGui.TableNextColumn();
        PlannerUi.MetricCard("metric-ready", FontAwesomeIcon.CheckCircle, ready.ToString(), "Ready", PlannerUi.Green);
        ImGui.TableNextColumn();
        PlannerUi.MetricCard("metric-warnings", FontAwesomeIcon.ExclamationTriangle, warnings.ToString(), "Needs attention", warnings > 0 ? PlannerUi.Amber : PlannerUi.Muted);
        ImGui.EndTable();
    }

    private void DrawResultsToolbar()
    {
        ImGui.SetNextItemWidth(Math.Min(230f * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X * 0.32f));
        ImGui.InputTextWithHint("##fc-search", "Search free companies…", ref this.fcSearch, 80);
        PlannerUi.Tooltip("Filter by free company name or world.");

        ImGui.SameLine();
        DrawFilterButton("Leveling", FcResultFilter.Leveling);
        ImGui.SameLine(0, 3f * ImGuiHelpers.GlobalScale);
        DrawFilterButton("All", FcResultFilter.All);
        ImGui.SameLine(0, 3f * ImGuiHelpers.GlobalScale);
        DrawFilterButton("Ready", FcResultFilter.Ready);

        ImGui.SameLine();
        if (PlannerUi.IconButton("collapse-all", FontAwesomeIcon.CompressAlt, "Collapse all free companies"))
            this.viewState.CollapseAll();
        ImGui.SameLine();
        if (PlannerUi.IconButton("expand-all", FontAwesomeIcon.ExpandAlt, "Expand all free companies"))
            this.viewState.ExpandAll();
    }

    private void DrawFilterButton(string label, FcResultFilter filter)
    {
        var selected = this.configuration.ResultsFilter == filter;
        if (PlannerUi.SegmentedButton($"filter-{filter}", label, selected) && !selected)
        {
            this.configuration.ResultsFilter = filter;
            this.saveConfiguration();
        }
    }

    private void DrawFcResult(EtaResult result)
    {
        if (this.viewState.ExpansionOverride is not null)
            ImGui.SetNextItemOpen(this.viewState.ExpansionOverride.Value, ImGuiCond.Always);

        var ready = ResultsViewState.IsReady(result, result.TargetRank);
        var statusText = result.IsComplete
            ? ready
                ? "Ready now"
                : $"Complete {FormatRelative(result.FcCompletionAtUtc, result.GeneratedAtUtc)}"
            : result.IncompleteReason?.Contains("time limit", StringComparison.OrdinalIgnoreCase) == true
                ? "Timed out"
                : "Incomplete";
        var statusColor = !result.IsComplete ? PlannerUi.Amber : ready ? PlannerUi.Green : PlannerUi.Cyan;
        var fcKey = Convert.ToHexString(result.FcId);

        ImGui.PushStyleColor(ImGuiCol.Header, PlannerUi.PanelBackground);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, PlannerUi.PanelBackgroundAlt);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, PlannerUi.PanelBackgroundAlt);
        var open = ImGui.CollapsingHeader($"{result.FcDisplayName}   •   {statusText}###fc-{fcKey}");
        ImGui.PopStyleColor(3);
        if (!open)
            return;

        PlannerUi.DrawStatusPill(statusText, statusColor);
        if (!result.IsComplete && result.IncompleteReason is not null)
        {
            ImGui.SameLine();
            ImGui.TextColored(PlannerUi.Amber, result.IncompleteReason);
        }

        var tableFlags = ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.RowBg |
                         ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollX | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable($"table-{fcKey}", 7, tableFlags, new Vector2(-1, 0), 860f * ImGuiHelpers.GlobalScale))
            return;

        ImGui.TableSetupColumn("Submarine", ImGuiTableColumnFlags.WidthStretch, 1.35f);
        ImGui.TableSetupColumn("Rank", ImGuiTableColumnFlags.WidthFixed, 95f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("ETA", ImGuiTableColumnFlags.WidthFixed, 92f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Voyages", ImGuiTableColumnFlags.WidthFixed, 72f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Build", ImGuiTableColumnFlags.WidthFixed, 72f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Next route", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 116f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableHeadersRow();

        foreach (var sub in result.PerSubResults.OrderBy(sub => sub.SubmarineName))
        {
            var subKey = $"{fcKey}:{sub.SubmarineId}";
            var subOpen = this.expandedSubmarines.Contains(subKey);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var chevron = subOpen ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight;
            var rowStart = ImGui.GetCursorScreenPos();
            var rowClicked = ImGui.Selectable(
                $"##row-{subKey}",
                false,
                ImGuiSelectableFlags.SpanAllColumns,
                new Vector2(0, ImGui.GetFrameHeight()));
            var rowHovered = ImGui.IsItemHovered();
            var rowEnd = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(rowStart + new Vector2(3f * ImGuiHelpers.GlobalScale, 1f * ImGuiHelpers.GlobalScale));
            PlannerUi.Icon(chevron, PlannerUi.Teal);
            ImGui.SameLine();
            ImGui.TextUnformatted(sub.SubmarineName);
            ImGui.SetCursorScreenPos(rowEnd);
            if (rowClicked)
            {
                if (!this.expandedSubmarines.Add(subKey))
                    this.expandedSubmarines.Remove(subKey);
            }
            if (rowHovered)
                ImGui.SetTooltip(subOpen ? "Hide voyage forecast" : "Show voyage forecast");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{sub.StartingRank} → {sub.FinalRank}");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(sub.StartingRank >= result.TargetRank ? "now" : FormatRelative(sub.EtaAtUtc, result.GeneratedAtUtc));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(sub.VoyageCount.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(sub.PlannedBuild);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatRoute(sub.NextRoute));
            ImGui.TableNextColumn();
            DrawSubmarineStatus(sub, result.TargetRank);
        }

        ImGui.EndTable();

        foreach (var sub in result.PerSubResults.OrderBy(sub => sub.SubmarineName))
        {
            var subKey = $"{fcKey}:{sub.SubmarineId}";
            if (!this.expandedSubmarines.Contains(subKey))
                continue;

            ImGui.PushID(subKey);
            ImGui.Indent(12f * ImGuiHelpers.GlobalScale);
            ImGui.Spacing();
            PlannerUi.IconText(FontAwesomeIcon.Ship, $"{sub.SubmarineName} voyage forecast", PlannerUi.Teal);
            DrawSubDetails(sub, this.configuration.Settings.ShowRouteDiagnostics);
            ImGui.Unindent(12f * ImGuiHelpers.GlobalScale);
            ImGui.PopID();
        }
    }

    private void DrawSubmarineStatus(PerSubEtaResult sub, int targetRank)
    {
        if (!sub.IsComplete || sub.Warnings.Count > 0)
        {
            var count = sub.Warnings.Count + (sub.IsComplete ? 0 : 1);
            PlannerUi.DrawStatusPill($"{count} warning{(count == 1 ? string.Empty : "s")}", PlannerUi.Amber);
            return;
        }

        if (this.configuration.Settings.ShowPost114MrojzReadiness && sub.PostTargetFarmingReady)
        {
            PlannerUi.DrawStatusPill("WSCC/MROJZ", PlannerUi.Green);
            return;
        }

        PlannerUi.DrawStatusPill(sub.StartingRank >= targetRank ? "Ready" : "Leveling", sub.StartingRank >= targetRank ? PlannerUi.Green : PlannerUi.Cyan);
    }

    private static void DrawSubDetails(PerSubEtaResult sub, bool showDiagnostics)
    {
        if (!sub.IsComplete && sub.IncompleteReason is not null)
            PlannerUi.Callout("sub-incomplete", FontAwesomeIcon.ExclamationTriangle, "Incomplete forecast", sub.IncompleteReason, PlannerUi.Amber);

        if (sub.Warnings.Count > 0)
        {
            PlannerUi.Callout(
                "sub-warnings",
                FontAwesomeIcon.ExclamationTriangle,
                "Warnings",
                string.Join("  •  ", sub.Warnings.Distinct()),
                PlannerUi.Amber);
        }

        if (sub.UnlockMilestones.Count > 0 && ImGui.CollapsingHeader("Unlock milestones"))
        {
            foreach (var milestone in sub.UnlockMilestones)
            {
                DrawBulletText(
                    $"{FormatMilestoneKind(milestone.Kind)}: {milestone.SourcePoint} → {milestone.UnlockedPoint} " +
                    $"at {milestone.ReturnAtUtc.LocalDateTime:g}");
            }
        }

        var columnCount = showDiagnostics ? 9 : 7;
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable |
                    ImGuiTableFlags.ScrollX | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("preview", columnCount, flags, new Vector2(-1, 0), (showDiagnostics ? 980f : 760f) * ImGuiHelpers.GlobalScale))
            return;

        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 34f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Returns", ImGuiTableColumnFlags.WidthFixed, 140f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Build", ImGuiTableColumnFlags.WidthFixed, 64f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Route", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("EXP", ImGuiTableColumnFlags.WidthFixed, 86f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Rank", ImGuiTableColumnFlags.WidthFixed, 82f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Repeats", ImGuiTableColumnFlags.WidthFixed, 65f * ImGuiHelpers.GlobalScale);
        if (showDiagnostics)
        {
            ImGui.TableSetupColumn("Per voyage", ImGuiTableColumnFlags.WidthFixed, 165f * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("EXP/h", ImGuiTableColumnFlags.WidthFixed, 82f * ImGuiHelpers.GlobalScale);
        }
        ImGui.TableHeadersRow();

        for (var i = 0; i < sub.VoyagePreview.Count; i++)
        {
            var plan = sub.VoyagePreview[i];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted((i + 1).ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plan.ReturnAtUtc.LocalDateTime.ToString("g"));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plan.BuildCode);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatRoute(plan.Route));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plan.ExpGain.ToString("N0"));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{plan.RankBefore}→{plan.RankAfter}");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plan.RepeatCount.ToString());
            if (showDiagnostics)
            {
                ImGui.TableNextColumn();
                var perDuration = plan.PerVoyageDuration == TimeSpan.Zero ? plan.Duration : plan.PerVoyageDuration;
                var perExp = plan.ExpPerVoyage == 0 ? plan.ExpGain : plan.ExpPerVoyage;
                ImGui.TextUnformatted($"{FormatDuration(perDuration)} / {perExp:N0} EXP");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(plan.ExpPerHour.ToString("N0"));
            }
        }

        ImGui.EndTable();
    }
}
