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
        var dependency = this.getSubmarineTrackerState();
        if (!dependency.IsAvailable)
        {
            PlannerUi.Callout(
                "submarine-tracker-dependency",
                FontAwesomeIcon.ExclamationTriangle,
                dependency.IsInstalled ? "Submarine Tracker is disabled" : "Submarine Tracker is required",
                dependency.IsInstalled
                    ? "Enable Submarine Tracker before refreshing the forecast. Existing results remain visible until then."
                    : "Install and enable Submarine Tracker to provide the fleet and voyage data used by this planner.",
                PlannerUi.Amber);
            if (PlannerUi.IconButtonWithText(
                    "open-submarine-tracker",
                    dependency.IsInstalled ? FontAwesomeIcon.ToggleOn : FontAwesomeIcon.Download,
                    dependency.IsInstalled ? "Open installed plugins" : "Find Submarine Tracker"))
            {
                this.openSubmarineTrackerInstaller(dependency.IsInstalled);
            }
            ImGui.Spacing();
            if (this.snapshot is null)
                return;
        }

        if (this.snapshot is null && this.refreshTask is null)
            StartRefresh();

        CheckForTrackerDataChanges();
        var refreshing = this.refreshTask is { IsCompleted: false };
        if (this.trackerDataChanged && !refreshing)
        {
            PlannerUi.Callout(
                "tracker-data-changed",
                FontAwesomeIcon.Database,
                "New SubmarineTracker data available",
                "Ranks, active voyages, or unlock data changed—or a voyage returned—after this forecast was calculated. Existing results remain visible until you refresh.",
                PlannerUi.Amber);
            if (PlannerUi.IconButtonWithText("refresh-tracker-data", FontAwesomeIcon.SyncAlt, "Refresh forecast"))
                QueueRefresh(ForecastRefreshMode.Incremental);
            ImGui.Spacing();
        }

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
            var title = this.refreshBaseSnapshot is null ? "Charting the first forecast" : "Refreshing forecast";
            var active = this.snapshot?.FcProgress.FirstOrDefault(progress => progress.Status == FcCalculationStatus.Calculating);
            var completed = this.snapshot?.FcProgress.Count(progress =>
                progress.Status is FcCalculationStatus.Complete or FcCalculationStatus.Partial or FcCalculationStatus.TimedOut or FcCalculationStatus.Failed) ?? 0;
            var total = this.snapshot?.FcProgress.Count(progress =>
                progress.Status is not (FcCalculationStatus.Reused or FcCalculationStatus.AwaitingTrackerUpdate)) ?? 0;
            var reused = this.snapshot?.FcProgress.Count(progress => progress.Status == FcCalculationStatus.Reused) ?? 0;
            var waiting = this.snapshot?.FcProgress.Count(progress => progress.Status == FcCalculationStatus.AwaitingTrackerUpdate) ?? 0;
            var incrementalPrefix = this.snapshot?.RefreshMode == ForecastRefreshMode.Incremental
                ? $"{total} changed · {reused} reused{(waiting > 0 ? $" · {waiting} waiting for tracker" : string.Empty)} · "
                : string.Empty;
            var body = active is null
                ? $"Reading SubmarineTracker data {FormatElapsed(elapsed)}"
                : incrementalPrefix + $"FC {Math.Min(completed + 1, total)} of {total}: {active.FcDisplayName} · " +
                  $"{FormatElapsed(DateTimeOffset.UtcNow - (active.StartedAtUtc ?? DateTimeOffset.UtcNow))}";
            if (reused > 0 && this.snapshot?.RefreshMode != ForecastRefreshMode.Incremental)
                body += $" · {reused} unchanged FC{(reused == 1 ? string.Empty : "s")} reused";
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
                $"{(currentSnapshot.IsRunning ? "Progress" : "Calculated")} in {metrics.ElapsedMilliseconds:N0} ms  •  {metrics.RouteQueries:N0} route queries  •  " +
                $"{metrics.RouteCacheHits:N0} cache hits  •  {metrics.RoutesEvaluated:N0} routes checked  •  " +
                $"{metrics.CalculatedFreeCompanies} calculated  •  {metrics.ReusedFreeCompanies} reused  •  " +
                $"{metrics.AwaitingTrackerFreeCompanies} waiting for tracker");
        }

        if (!currentSnapshot.IsRunning && (currentSnapshot.Warnings.Count > 0 || !currentSnapshot.IsComplete))
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
                string.Join("  •  ", warningText.Distinct()),
                PlannerUi.Amber);
        }

        var resultsByFc = currentSnapshot.Results.ToDictionary(result => Convert.ToHexString(result.FcId));
        var progressByFc = currentSnapshot.FcProgress.ToDictionary(progress => progress.FcIdKey);
        var visibleEntries = currentSnapshot.FreeCompanies
            .Where(fc => ShouldIncludeFc(fc, this.configuration.Settings.TargetRank, this.configuration.ResultsFilter))
            .Where(fc => string.IsNullOrWhiteSpace(this.fcSearch) ||
                         fc.DisplayName.Contains(this.fcSearch, StringComparison.OrdinalIgnoreCase))
            .OrderBy(fc => fc.DisplayName)
            .Select(fc => (
                Fc: fc,
                Result: resultsByFc.GetValueOrDefault(fc.FcIdKey),
                Progress: progressByFc.GetValueOrDefault(fc.FcIdKey)))
            .ToArray();

        ImGui.Spacing();
        ImGui.TextColored(PlannerUi.Muted, $"{visibleEntries.Length} shown of {currentSnapshot.FreeCompanies.Count} tracked free companies");
        ImGui.Spacing();

        if (currentSnapshot.FreeCompanies.Count == 0)
        {
            PlannerUi.Callout(
                "empty-data",
                FontAwesomeIcon.Database,
                "No SubmarineTracker data found",
                "Check the Data source page if your tracker database is stored outside its normal location.",
                PlannerUi.Amber);
            return;
        }

        if (visibleEntries.Length == 0)
        {
            PlannerUi.Callout(
                "empty-filter",
                FontAwesomeIcon.Search,
                "No matching fleets",
                "Change the search text or choose another readiness filter.",
                PlannerUi.Cyan);
            return;
        }

        foreach (var entry in visibleEntries)
        {
            if (entry.Result is not null)
                DrawFcResult(entry.Result, entry.Progress);
            else
                DrawPendingFc(entry.Fc, entry.Progress);
            ImGui.Spacing();
        }

        this.viewState.ClearExpansionOverride();
    }

    private void DrawSummaryCards(EtaPlannerSnapshot currentSnapshot)
    {
        var total = currentSnapshot.FreeCompanies.Count;
        var leveling = currentSnapshot.FreeCompanies.Count(fc =>
            !IsReadyNow(fc, this.configuration.Settings.TargetRank));
        var ready = total - leveling;
        var warnings = currentSnapshot.FcProgress.Count(progress =>
            progress.Status is FcCalculationStatus.Partial or FcCalculationStatus.TimedOut or FcCalculationStatus.Failed or FcCalculationStatus.AwaitingTrackerUpdate);

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

    private void DrawFcResult(EtaResult result, FcCalculationProgress? calculationProgress = null)
    {
        if (this.viewState.ExpansionOverride is not null)
            ImGui.SetNextItemOpen(this.viewState.ExpansionOverride.Value, ImGuiCond.Always);

        var ready = ResultsViewState.IsReady(result, result.TargetRank);
        var resultStatusText = result.IsComplete
            ? ready
                ? "Ready now"
                : $"Median {FormatRelative(result.FcCompletionAtUtc, DateTimeOffset.UtcNow)}"
            : result.IncompleteReason?.Contains("time limit", StringComparison.OrdinalIgnoreCase) == true
                ? "Timed out"
                : "Incomplete";
        var isRefreshingFc = calculationProgress?.Status is FcCalculationStatus.Queued or FcCalculationStatus.Calculating;
        var statusText = calculationProgress?.Status switch
        {
            FcCalculationStatus.Queued => "Queued for refresh",
            FcCalculationStatus.Calculating => $"Refreshing {FormatProgressElapsed(calculationProgress)}",
            FcCalculationStatus.Reused => "Up to date",
            FcCalculationStatus.AwaitingTrackerUpdate => "Waiting for SubmarineTracker",
            FcCalculationStatus.TimedOut => "Timed out",
            FcCalculationStatus.Failed => "Refresh failed",
            _ => resultStatusText,
        };
        var statusColor = calculationProgress?.Status is FcCalculationStatus.TimedOut or FcCalculationStatus.Failed or FcCalculationStatus.AwaitingTrackerUpdate
            ? PlannerUi.Amber
            : !result.IsComplete ? PlannerUi.Amber : ready ? PlannerUi.Green : PlannerUi.Cyan;
        var fcKey = Convert.ToHexString(result.FcId);

        ImGui.PushStyleColor(ImGuiCol.Header, PlannerUi.PanelBackground);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, PlannerUi.PanelBackgroundAlt);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, PlannerUi.PanelBackgroundAlt);
        var open = ImGui.CollapsingHeader($"{result.FcDisplayName}   •   {statusText}###fc-{fcKey}");
        ImGui.PopStyleColor(3);
        if (!open)
            return;

        PlannerUi.DrawStatusPill(resultStatusText, !result.IsComplete ? PlannerUi.Amber : ready ? PlannerUi.Green : PlannerUi.Cyan);
        if (calculationProgress?.Status is FcCalculationStatus.Queued or FcCalculationStatus.Calculating or FcCalculationStatus.Reused or FcCalculationStatus.AwaitingTrackerUpdate or FcCalculationStatus.TimedOut or FcCalculationStatus.Failed)
        {
            ImGui.SameLine();
            PlannerUi.DrawStatusPill(statusText, statusColor);
            if (calculationProgress.Status == FcCalculationStatus.Reused && ImGui.IsItemHovered())
                ImGui.SetTooltip($"Forecast originally calculated {result.GeneratedAtUtc.LocalDateTime:g}.");
        }
        if (!ready && result.CompletionForecast is not null)
        {
            ImGui.SameLine();
            ImGui.TextColored(
                PlannerUi.Muted,
                $"Likely {FormatRelative(result.CompletionForecast.P10AtUtc, DateTimeOffset.UtcNow)}–" +
                $"{FormatRelative(result.CompletionForecast.P90AtUtc, DateTimeOffset.UtcNow)} · {result.ProbabilitySampleCount} samples");
        }
        if (!result.IsComplete && result.IncompleteReason is not null)
        {
            ImGui.SameLine();
            ImGui.TextColored(PlannerUi.Amber, result.IncompleteReason);
        }

        if (isRefreshingFc)
        {
            PlannerUi.Callout(
                $"refreshing-fc-{fcKey}",
                FontAwesomeIcon.SyncAlt,
                calculationProgress!.Status == FcCalculationStatus.Calculating ? "Refreshing this FC" : "Waiting for its turn",
                calculationProgress.Status == FcCalculationStatus.Calculating
                    ? $"The previous result remains visible while a new forecast is calculated ({FormatProgressElapsed(calculationProgress)})."
                    : "The previous result remains visible until sequential calculation reaches this FC.",
                PlannerUi.Cyan);
        }
        else if (calculationProgress?.Status is FcCalculationStatus.TimedOut or FcCalculationStatus.Failed)
        {
            PlannerUi.Callout(
                $"fc-calculation-notice-{fcKey}",
                FontAwesomeIcon.ExclamationTriangle,
                calculationProgress.Status == FcCalculationStatus.TimedOut ? "Per-FC timeout reached" : "FC forecast failed",
                calculationProgress.Message ?? "The previous or partial result remains visible.",
                PlannerUi.Amber);
        }
        else if (calculationProgress?.Status == FcCalculationStatus.AwaitingTrackerUpdate)
        {
            PlannerUi.Callout(
                $"fc-awaiting-tracker-{fcKey}",
                FontAwesomeIcon.Database,
                "Waiting for SubmarineTracker",
                calculationProgress.Message ?? "The last complete forecast remains visible until the returned voyage is written to the tracker database.",
                PlannerUi.Amber);
        }
        if (result.ActiveUnlockAttempts.Count > 0)
        {
            var attempts = result.ActiveUnlockAttempts.Select(attempt =>
                $"{FormatPoint(attempt.TargetPoint)} via {FormatPoint(attempt.SourcePoint)}: " +
                $"{attempt.SubmarineIds.Count} submarine{(attempt.SubmarineIds.Count == 1 ? string.Empty : "s")} " +
                $"({string.Join(", ", attempt.SubmarineNames)}) · " +
                $"{attempt.CombinedSuccessProbability:P0} by {attempt.LatestReturnAtUtc.LocalDateTime:g}");
            PlannerUi.Callout(
                $"active-unlocks-{fcKey}",
                FontAwesomeIcon.Dice,
                "Unlocks in progress",
                string.Join("\n", attempts),
                PlannerUi.Amber);
        }

        var minimumTableWidth = 860f * ImGuiHelpers.GlobalScale;
        var needsHorizontalScroll = ImGui.GetContentRegionAvail().X < minimumTableWidth;
        var tableFlags = ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.RowBg |
                         ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;
        if (needsHorizontalScroll)
            tableFlags |= ImGuiTableFlags.ScrollX;

        var tableSize = needsHorizontalScroll
            ? new Vector2(-1, CalculateTableHeight(result.PerSubResults.Count, true))
            : new Vector2(-1, CalculateTableHeight(result.PerSubResults.Count, false));
        if (!ImGui.BeginTable(
                $"table-{fcKey}",
                7,
                tableFlags,
                tableSize,
                needsHorizontalScroll ? minimumTableWidth : 0f))
            return;

        ImGui.TableSetupColumn("Submarine", ImGuiTableColumnFlags.WidthStretch, 1.35f);
        ImGui.TableSetupColumn("Rank", ImGuiTableColumnFlags.WidthFixed, 95f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("ETA", ImGuiTableColumnFlags.WidthFixed, 92f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Voyages", ImGuiTableColumnFlags.WidthFixed, 72f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Build", ImGuiTableColumnFlags.WidthFixed, 72f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Next after return", ImGuiTableColumnFlags.WidthStretch, 1f);
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
            ImGui.TextUnformatted(sub.StartingRank >= result.TargetRank ? "now" : $"P50 {FormatRelative(sub.EtaAtUtc, DateTimeOffset.UtcNow)}");
            if (sub.EtaForecast is not null && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    $"Likely range: {FormatRelative(sub.EtaForecast.P10AtUtc, DateTimeOffset.UtcNow)}–" +
                    $"{FormatRelative(sub.EtaForecast.P90AtUtc, DateTimeOffset.UtcNow)} ({sub.EtaForecast.SampleCount} samples)\n" +
                    $"Forecast calculated: {result.GeneratedAtUtc.LocalDateTime:g}");
            }
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(sub.VoyageCount.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(sub.PlannedBuild);
            ImGui.TableNextColumn();
            DrawNextRoute(sub);
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

        PlannerUi.DrawStatusPill(sub.StartingRank >= targetRank ? "Ready" : "Leveling", sub.StartingRank >= targetRank ? PlannerUi.Green : PlannerUi.Cyan);
    }

    private void DrawSubDetails(PerSubEtaResult sub, bool showDiagnostics)
    {
        if (sub.CurrentRoute.Count > 0 && sub.CurrentReturnAtUtc is not null)
        {
            PlannerUi.Callout(
                "current-voyage",
                FontAwesomeIcon.Ship,
                $"Current voyage · returns {sub.CurrentReturnAtUtc.Value.LocalDateTime:g}",
                FormatRoute(sub.CurrentRoute),
                PlannerUi.Cyan);
        }

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
                    $"{FormatMilestoneKind(milestone.Kind)}: {FormatPoint(milestone.SourcePoint)} → {FormatPoint(milestone.UnlockedPoint)} " +
                    $"at {milestone.ReturnAtUtc.LocalDateTime:g}");
            }
        }

        var columnCount = showDiagnostics ? 9 : 7;
        var minimumTableWidth = (showDiagnostics ? 980f : 760f) * ImGuiHelpers.GlobalScale;
        var needsHorizontalScroll = ImGui.GetContentRegionAvail().X < minimumTableWidth;
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable |
                    ImGuiTableFlags.SizingStretchProp;
        if (needsHorizontalScroll)
            flags |= ImGuiTableFlags.ScrollX;

        var tableSize = needsHorizontalScroll
            ? new Vector2(-1, CalculateTableHeight(sub.VoyagePreview.Count, true))
            : new Vector2(-1, CalculateTableHeight(sub.VoyagePreview.Count, false));
        if (!ImGui.BeginTable(
                "preview",
                columnCount,
                flags,
                tableSize,
                needsHorizontalScroll ? minimumTableWidth : 0f))
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
            DrawRoute(plan.Route);
            if (plan.DependsOnProjectedUnlocks)
            {
                ImGui.SameLine();
                ImGui.TextColored(PlannerUi.Amber, "projected");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(
                        $"Available only after the forecasted unlock of " +
                        string.Join(", ", plan.RequiredProjectedUnlocks.Select(FormatPoint)) + ".");
                }
            }
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

    private void DrawPendingFc(FcState fc, FcCalculationProgress? progress)
    {
        var fcKey = fc.FcIdKey;
        var status = progress?.Status ?? FcCalculationStatus.Queued;
        var statusText = status switch
        {
            FcCalculationStatus.Calculating => $"Calculating {FormatProgressElapsed(progress)}",
            FcCalculationStatus.Reused => "Up to date",
            FcCalculationStatus.AwaitingTrackerUpdate => "Waiting for SubmarineTracker",
            FcCalculationStatus.TimedOut => "Timed out",
            FcCalculationStatus.Failed => "Failed",
            FcCalculationStatus.Cancelled => "Cancelled",
            _ => "Queued",
        };
        var color = status is FcCalculationStatus.TimedOut or FcCalculationStatus.Failed or FcCalculationStatus.AwaitingTrackerUpdate
            ? PlannerUi.Amber
            : PlannerUi.Cyan;

        ImGui.PushStyleColor(ImGuiCol.Header, PlannerUi.PanelBackground);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, PlannerUi.PanelBackgroundAlt);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, PlannerUi.PanelBackgroundAlt);
        var open = ImGui.CollapsingHeader($"{fc.DisplayName}   •   {statusText}###fc-pending-{fcKey}");
        ImGui.PopStyleColor(3);
        if (!open)
            return;

        PlannerUi.DrawStatusPill(statusText, color);
        ImGui.SameLine();
        ImGui.TextColored(
            PlannerUi.Muted,
            $"{fc.Submarines.Count} submarine{(fc.Submarines.Count == 1 ? string.Empty : "s")} tracked");
        PlannerUi.Callout(
            $"fc-pending-callout-{fcKey}",
            status == FcCalculationStatus.Calculating ? FontAwesomeIcon.SyncAlt : FontAwesomeIcon.Clock,
            status == FcCalculationStatus.Calculating ? "Forecasting this FC" : "Sequential forecast",
            progress?.Message ?? "This FC will begin after the current calculation finishes or times out.",
            color);
    }

    private static bool ShouldIncludeFc(FcState fc, int targetRank, FcResultFilter filter)
        => filter switch
        {
            FcResultFilter.Leveling => !IsReadyNow(fc, targetRank),
            FcResultFilter.Ready => IsReadyNow(fc, targetRank),
            _ => true,
        };

    private static bool IsReadyNow(FcState fc, int targetRank)
        => fc.Submarines.Count > 0 && fc.Submarines.All(submarine => submarine.Rank >= targetRank);

    private static string FormatProgressElapsed(FcCalculationProgress? progress)
    {
        if (progress?.StartedAtUtc is null)
            return string.Empty;

        var end = progress.CompletedAtUtc ?? DateTimeOffset.UtcNow;
        return FormatElapsed(end - progress.StartedAtUtc.Value);
    }

    private void DrawNextRoute(PerSubEtaResult sub)
    {
        var outcomes = sub.NextRouteOutcomes.Where(outcome => outcome.Route.Count > 0).ToArray();
        var conditional = outcomes.Length > 1 || outcomes.Any(outcome => outcome.RequiredProjectedUnlocks.Count > 0);
        if (!conditional)
        {
            DrawRoute(outcomes.FirstOrDefault()?.Route ?? sub.NextRoute);
            return;
        }

        ImGui.TextColored(PlannerUi.Amber, outcomes.Length > 1 ? "Depends on unlock" : "Projected after unlock");
        if (!ImGui.IsItemHovered())
            return;

        ImGui.BeginTooltip();
        ImGui.TextColored(PlannerUi.Teal, "Next-route outcomes");
        ImGui.Separator();
        foreach (var outcome in outcomes)
        {
            ImGui.TextUnformatted($"{outcome.Probability:P0}  {FormatRoute(outcome.Route)}");
            if (outcome.RequiredProjectedUnlocks.Count > 0)
            {
                ImGui.TextColored(
                    PlannerUi.Amber,
                    $"Requires: {string.Join(", ", outcome.RequiredProjectedUnlocks.Select(FormatPoint))}");
            }
        }
        ImGui.EndTooltip();
    }

    private static float CalculateTableHeight(int rowCount, bool hasHorizontalScrollbar)
    {
        var style = ImGui.GetStyle();
        var headerHeight = ImGui.GetTextLineHeight() + (style.CellPadding.Y * 2f);
        var rowHeight = ImGui.GetFrameHeight() + (style.CellPadding.Y * 2f);
        return headerHeight
               + (Math.Max(0, rowCount) * rowHeight)
               + (hasHorizontalScrollbar ? style.ScrollbarSize : 0f)
               + (4f * ImGuiHelpers.GlobalScale);
    }
}
