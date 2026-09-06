using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using SubmarineEtaPlanner.Planner;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

public sealed partial class PlannerWindow
{
    private void DrawIncomePage()
    {
        var currentSnapshot = EnsureFleetSnapshot();
        if (currentSnapshot is null)
            return;

        DrawFleetNotices(currentSnapshot);
        PlannerUi.WrappedText("Recorded gross NPC salvage value · history coverage begins with tracked salvaged accessories.", PlannerUi.Muted);
        PlannerUi.Tooltip("Recorded average spreads historical gross gil over the covered period. Costs are not deducted; these are past observations, not guaranteed income.");
        if (this.incomeFcScope is { } scope)
        {
            var selected = currentSnapshot.FreeCompanies.FirstOrDefault(fc => fc.FcIdKey == scope);
            if (selected is null) this.incomeFcScope = null;
            else
            {
                PlannerUi.WrappedText($"FC scope: {selected.DisplayName}", PlannerUi.Teal);
                if (ImGui.SmallButton("Show all FCs##clear-income-scope")) this.incomeFcScope = null;
            }
        }
        ImGui.BeginDisabled(this.incomeFcScope is not null);
        DrawIncomeViewButton("All fleets", IncomeView.AllFleets);
        PlannerUi.SameLineIfFits("Leveling");
        DrawIncomeViewButton("Leveling", IncomeView.Leveling);
        PlannerUi.SameLineIfFits("Farming");
        DrawIncomeViewButton("Farming", IncomeView.Farming);
        ImGui.EndDisabled();
        PlannerUi.SameLineIfFits("", 190f * ImGuiHelpers.GlobalScale);
        DrawIncomeSortCombo();
        ImGui.Spacing();
        DrawIncomePeriodButtons();

        var now = DateTimeOffset.UtcNow;
        var period = GetIncomePeriod();
        var allProjections = CreateProjections(currentSnapshot, now);
        var requiredMode = IncomeViewPreferences.RequiredMode(this.configuration.IncomeView);
        var filteredProjections = allProjections
            .Where(projection => this.incomeFcScope is { } scopeId
                ? projection.State.FcIdKey == scopeId : FleetPresentationFiltering.Includes(projection, requiredMode))
            .ToArray();
        var projections = filteredProjections.ToDictionary(item => item.State.FcIdKey);
        var includedFcIds = projections.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var metrics = IncomeMetricsOrdering.Order(
            currentSnapshot.FreeCompanies
            .Where(fc => includedFcIds.Contains(fc.FcIdKey))
            .Select(fc => IncomeMetricsCalculator.Calculate(fc, now, period, this.catalog)),
            this.configuration.IncomeSort,
            metric => this.configuration.GetFcPreferences(metric.FcIdKey).Favorite);

        DrawIncomeSummary(metrics, now, period);
        ImGui.Spacing();
        var modeLabel = this.incomeFcScope is not null ? "selected FC · all roles" : this.configuration.IncomeView switch
        {
            IncomeView.Leveling => "with leveling submarines",
            IncomeView.Farming => "with farming submarines",
            _ => "all roles",
        };
        ImGui.TextColored(
            PlannerUi.Muted,
            $"{metrics.Count} FC{(metrics.Count == 1 ? string.Empty : "s")} shown of {allProjections.Count} tracked · {modeLabel}");
        if (metrics.Count == 0)
        {
            ImGui.Spacing();
            PlannerUi.Callout(
                "income-empty-filter",
                FontAwesomeIcon.InfoCircle,
                "No free companies match this filter",
                "Choose another role filter to include tracked income data.",
                PlannerUi.Muted);
            return;
        }

        ImGui.Spacing();
        var incomeHeaders = metrics.ToDictionary(
            metric => metric.FcIdKey,
            metric => IncomeFcHeaderPresentation.Create(
                projections[metric.FcIdKey],
                metric,
                false));
        var incomeLayout = CalculateIncomeHeaderLayout(incomeHeaders.Values, ImGui.GetContentRegionAvail().X - FavoriteControlWidth);
        DrawIncomeHeaderLegend(incomeLayout);
        foreach (var metric in metrics)
        {
            var projection = projections[metric.FcIdKey];
            DrawIncomeFleetGroup(projection, metric, incomeHeaders[metric.FcIdKey], incomeLayout);
        }
    }

    private void DrawIncomeFleetGroup(
        FcOperationalProjection projection,
        IncomeFcMetrics metric,
        IncomeFcHeaderPresentation presentation,
        CompactFcHeaderLayout layout)
    {
        ImGui.Spacing();
        DrawFavoriteControl(projection.State.FcIdKey);
        if (this.expandIncomeFc == projection.State.FcIdKey)
        {
            ImGui.SetNextItemOpen(true, ImGuiCond.Always);
            this.expandIncomeFc = null;
        }
        var origin = ImGui.GetCursorScreenPos();
        var style = ImGui.GetStyle();
        var paddingY = Math.Max(0f, (layout.HeaderHeight - ImGui.GetTextLineHeight()) / 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(style.FramePadding.X, paddingY));
        ImGui.PushStyleColor(ImGuiCol.Header, PlannerUi.PanelBackgroundAlt);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(PlannerUi.PanelBackgroundAlt.X + 0.02f, PlannerUi.PanelBackgroundAlt.Y + 0.05f, PlannerUi.PanelBackgroundAlt.Z + 0.05f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(PlannerUi.PanelBackgroundAlt.X + 0.03f, PlannerUi.PanelBackgroundAlt.Y + 0.08f, PlannerUi.PanelBackgroundAlt.Z + 0.08f, 1f));
        var open = ImGui.CollapsingHeader($"###{presentation.WidgetId}");
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();
        DrawIncomeHeaderFields(origin, layout, presentation);
        DrawIncomeHeaderTooltip(projection, metric);
        if (!open)
            return;

        ImGui.Spacing();
        DrawFcShortcuts(projection.State.FcIdKey);
        PlannerUi.WrappedText($"Recorded coverage: {FormatIncomeDate(metric.FirstReturnAtUtc)} – {FormatIncomeDate(metric.LastReturnAtUtc)} · Gross NPC value; costs are not deducted.", PlannerUi.Muted);
        DrawIncomeSubmarineTable(metric);
    }

    private void DrawIncomeSubmarineTable(IncomeFcMetrics metric)
    {
        var layout = CalculateResponsiveTableLayout(
            ImGui.GetContentRegionAvail().X,
            new ResponsiveTableColumn("Submarine", metric.Submarines.Select(submarine => submarine.Name), 120, 220, Flexible: true, FillRemaining: true),
            new ResponsiveTableColumn("Rank", metric.Submarines.Select(submarine => $"R{submarine.Rank}"), 68, 90),
            new ResponsiveTableColumn("Build", metric.Submarines.Select(submarine => submarine.CurrentBuild.Code), 72, 100),
            new ResponsiveTableColumn("Gross gil", metric.Submarines.Select(submarine => $"{submarine.GrossGil:N0}"), 105, 175),
            new ResponsiveTableColumn("Avg / day", metric.Submarines.Select(submarine => $"{submarine.RecordedAverageGilPerDay:N0}"), 125, 175),
            new ResponsiveTableColumn("Voyages", metric.Submarines.Select(submarine => submarine.VoyageCount.ToString("N0")), 82, 105),
            new ResponsiveTableColumn("Gil/voyage", metric.Submarines.Select(submarine => $"{submarine.GilPerVoyage:N0}"), 105, 155),
            new ResponsiveTableColumn("First return", metric.Submarines.Select(submarine => FormatIncomeDate(submarine.FirstReturnAtUtc)), 125, 170),
            new ResponsiveTableColumn("Last return", metric.Submarines.Select(submarine => FormatIncomeDate(submarine.LastReturnAtUtc)), 125, 170));
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit;
        if (layout.RequiresHorizontalScroll)
            flags |= ImGuiTableFlags.ScrollX;
        var tableHeight = CalculateTableHeight(metric.Submarines.Count, layout.RequiresHorizontalScroll);
        if (!ImGui.BeginTable(
            $"income-table-{metric.FcIdKey}",
            9,
            flags,
            new Vector2(-1, tableHeight),
            layout.RequiresHorizontalScroll ? layout.InnerWidth : 0f))
            return;

        SetupResponsiveTableColumns(layout);
        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableHeadersRow();
        foreach (var submarine in metric.Submarines)
        {
            ImGui.TableNextRow();
            DrawTableText(submarine.Name);
            DrawTableText($"R{submarine.Rank}");
            ImGui.TableNextColumn();
            DrawCurrentBuild(submarine.CurrentBuild);
            DrawTableText($"{submarine.GrossGil:N0}", rightAligned: true);
            DrawTableText($"{submarine.RecordedAverageGilPerDay:N0}", rightAligned: true);
            DrawTableText(submarine.VoyageCount.ToString("N0"), rightAligned: true);
            DrawTableText($"{submarine.GilPerVoyage:N0}", rightAligned: true);
            DrawTableText(FormatIncomeDate(submarine.FirstReturnAtUtc));
            DrawTableText(FormatIncomeDate(submarine.LastReturnAtUtc));
        }
        ImGui.EndTable();
    }

    private void DrawIncomeSummary(IReadOnlyList<IncomeFcMetrics> metrics, DateTimeOffset now, TimeSpan? period)
    {
        var summary = IncomeMetricsCalculator.Summarize(metrics, now, period);
        if (!ImGui.BeginTable("income-summary", ImGui.GetContentRegionAvail().X < 680f * ImGuiHelpers.GlobalScale ? 2 : 4, ImGuiTableFlags.SizingStretchSame))
            return;
        ImGui.TableNextColumn(); PlannerUi.MetricCard("income-gross", FontAwesomeIcon.Coins, ResultsViewState.FormatCompactGil(summary.GrossGil), "Gross gil", PlannerUi.Green);
        ImGui.TableNextColumn(); PlannerUi.MetricCard("income-recorded-average", FontAwesomeIcon.CalendarDay, summary.CoveredDays == 0 ? "—" : ResultsViewState.FormatCompactGil((long)summary.RecordedAverageGilPerDay), "Recorded avg / day", PlannerUi.Teal);
        ImGui.TableNextColumn(); PlannerUi.MetricCard("income-voyage", FontAwesomeIcon.Ship, summary.VoyageCount == 0 ? "—" : ResultsViewState.FormatCompactGil((long)summary.GilPerVoyage), "Gil / voyage", PlannerUi.Cyan);
        ImGui.TableNextColumn(); PlannerUi.MetricCard("income-fcs", FontAwesomeIcon.Building, summary.FcCount.ToString(), $"FCs shown · {summary.CoveredDays:0.#} days", PlannerUi.Muted);
        ImGui.EndTable();
    }

    private void DrawIncomePeriodButtons()
    {
        DrawIncomePeriodButton("7 days", IncomePeriod.Days7);
        PlannerUi.SameLineIfFits("30 days");
        DrawIncomePeriodButton("30 days", IncomePeriod.Days30);
        PlannerUi.SameLineIfFits("90 days");
        DrawIncomePeriodButton("90 days", IncomePeriod.Days90);
        PlannerUi.SameLineIfFits("1 year");
        DrawIncomePeriodButton("1 year", IncomePeriod.Days365);
        PlannerUi.SameLineIfFits("Lifetime");
        DrawIncomePeriodButton("Lifetime", IncomePeriod.Lifetime);
    }

    private void DrawIncomeViewButton(string label, IncomeView view)
    {
        if (PlannerUi.SegmentedButton($"income-view-{view}", label, this.configuration.IncomeView == view))
        {
            this.configuration.IncomeView = view;
            this.saveConfiguration();
        }
    }

    private void DrawIncomePeriodButton(string label, IncomePeriod period)
    {
        if (PlannerUi.SegmentedButton($"income-period-{period}", label, this.configuration.IncomePeriod == period))
        {
            this.configuration.IncomePeriod = period;
            this.saveConfiguration();
        }
    }

    private void DrawIncomeSortCombo()
    {
        string[] labels = ["Gross gil", "Recorded avg / day", "Gil / voyage", "FC name"];
        var value = this.configuration.IncomeSort;
        if (DrawEnumCombo("##income-sort", labels, ref value, 190f))
        {
            this.configuration.IncomeSort = value;
            this.saveConfiguration();
        }
    }

    private TimeSpan? GetIncomePeriod() => this.configuration.IncomePeriod switch
    {
        IncomePeriod.Days7 => TimeSpan.FromDays(7),
        IncomePeriod.Days30 => TimeSpan.FromDays(30),
        IncomePeriod.Days90 => TimeSpan.FromDays(90),
        IncomePeriod.Days365 => TimeSpan.FromDays(365),
        _ => null,
    };
}
