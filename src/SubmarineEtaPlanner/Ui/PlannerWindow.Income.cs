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

        PlannerUi.Callout(
            "income-definition",
            FontAwesomeIcon.InfoCircle,
            "Recorded gross NPC salvage value",
            "The fleet filter uses each FC's current mode. Values include all recorded tracker returns in the selected period, so a Farming FC may include voyages from before it reached its target rank.",
            PlannerUi.Teal);
        ImGui.Spacing();
        DrawIncomeViewButton("All fleets", IncomeView.AllFleets);
        ImGui.SameLine(0, 3f * ImGuiHelpers.GlobalScale);
        DrawIncomeViewButton("Leveling", IncomeView.Leveling);
        ImGui.SameLine(0, 3f * ImGuiHelpers.GlobalScale);
        DrawIncomeViewButton("Farming", IncomeView.Farming);
        ImGui.SameLine();
        DrawIncomeSortCombo();
        ImGui.Spacing();
        DrawIncomePeriodButtons();

        var now = DateTimeOffset.UtcNow;
        var period = GetIncomePeriod();
        var allProjections = CreateProjections(currentSnapshot, now);
        var requiredMode = IncomeViewPreferences.RequiredMode(this.configuration.IncomeView);
        var filteredProjections = allProjections
            .Where(projection => FleetPresentationFiltering.Includes(projection, requiredMode))
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
        var modeLabel = this.configuration.IncomeView switch
        {
            IncomeView.Leveling => "currently leveling",
            IncomeView.Farming => "currently farming",
            _ => "all modes",
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
                "Choose another fleet mode to include tracked income data.",
                PlannerUi.Muted);
            return;
        }

        ImGui.Spacing();
        var incomeHeaders = metrics.ToDictionary(
            metric => metric.FcIdKey,
            metric => IncomeFcHeaderPresentation.Create(
                projections[metric.FcIdKey],
                metric,
                this.configuration.GetFcPreferences(metric.FcIdKey).Favorite));
        var incomeLayout = CalculateIncomeHeaderLayout(incomeHeaders.Values, ImGui.GetContentRegionAvail().X);
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
        IncomeHeaderLayout layout)
    {
        ImGui.Spacing();
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
        DrawIncomeHeaderFields(origin, layout, presentation, legend: false);
        DrawIncomeHeaderTooltip(projection, metric);
        if (!open)
            return;

        ImGui.Spacing();
        DrawIncomeSubmarineTable(metric);
    }

    private static void DrawIncomeSubmarineTable(IncomeFcMetrics metric)
    {
        const float minimumWidth = 1_080f;
        var scaledMinimumWidth = minimumWidth * ImGuiHelpers.GlobalScale;
        var needsHorizontalScroll = ImGui.GetContentRegionAvail().X < scaledMinimumWidth;
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;
        if (needsHorizontalScroll)
            flags |= ImGuiTableFlags.ScrollX;
        var tableHeight = CalculateTableHeight(metric.Submarines.Count, needsHorizontalScroll);
        if (!ImGui.BeginTable(
                $"income-table-{metric.FcIdKey}",
                9,
                flags,
                new Vector2(-1, tableHeight),
                needsHorizontalScroll ? scaledMinimumWidth : 0f))
            return;

        ImGui.TableSetupColumn("Submarine", ImGuiTableColumnFlags.WidthFixed, 165f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Rank", ImGuiTableColumnFlags.WidthFixed, 68f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Build", ImGuiTableColumnFlags.WidthFixed, 72f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Gross gil", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("Gil/day", ImGuiTableColumnFlags.WidthFixed, 105f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Voyages", ImGuiTableColumnFlags.WidthFixed, 82f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Gil/voyage", ImGuiTableColumnFlags.WidthFixed, 110f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("First return", ImGuiTableColumnFlags.WidthFixed, 140f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Last return", ImGuiTableColumnFlags.WidthFixed, 140f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableHeadersRow();
        foreach (var submarine in metric.Submarines)
        {
            ImGui.TableNextRow();
            DrawTableText(submarine.Name);
            DrawTableText($"R{submarine.Rank}");
            ImGui.TableNextColumn();
            DrawCurrentBuild(submarine.CurrentBuild);
            DrawTableText($"{submarine.GrossGil:N0}");
            DrawTableText($"{submarine.GilPerDay:N0}");
            DrawTableText(submarine.ValidVoyages.ToString("N0"));
            DrawTableText($"{submarine.GilPerVoyage:N0}");
            DrawTableText(FormatIncomeDate(submarine.FirstReturnAtUtc));
            DrawTableText(FormatIncomeDate(submarine.LastReturnAtUtc));
        }
        ImGui.EndTable();
    }

    private void DrawIncomeSummary(IReadOnlyList<IncomeFcMetrics> metrics, DateTimeOffset now, TimeSpan? period)
    {
        var summary = IncomeMetricsCalculator.Summarize(metrics, now, period);
        if (!ImGui.BeginTable("income-summary", 4, ImGuiTableFlags.SizingStretchSame))
            return;
        ImGui.TableNextColumn(); PlannerUi.MetricCard("income-gross", FontAwesomeIcon.Coins, ResultsViewState.FormatCompactGil(summary.GrossGil), "Gross gil", PlannerUi.Green);
        ImGui.TableNextColumn(); PlannerUi.MetricCard("income-day", FontAwesomeIcon.CalendarDay, summary.CoveredDays == 0 ? "—" : ResultsViewState.FormatCompactGil((long)summary.GilPerDay), "Gil / day", PlannerUi.Teal);
        ImGui.TableNextColumn(); PlannerUi.MetricCard("income-voyage", FontAwesomeIcon.Ship, summary.VoyageCount == 0 ? "—" : ResultsViewState.FormatCompactGil((long)summary.GilPerVoyage), "Gil / voyage", PlannerUi.Cyan);
        ImGui.TableNextColumn(); PlannerUi.MetricCard("income-fcs", FontAwesomeIcon.Building, summary.FcCount.ToString(), $"FCs shown · {summary.CoveredDays:0.#} days", PlannerUi.Muted);
        ImGui.EndTable();
    }

    private void DrawIncomePeriodButtons()
    {
        DrawIncomePeriodButton("7 days", IncomePeriod.Days7);
        ImGui.SameLine(0, 3f * ImGuiHelpers.GlobalScale);
        DrawIncomePeriodButton("30 days", IncomePeriod.Days30);
        ImGui.SameLine(0, 3f * ImGuiHelpers.GlobalScale);
        DrawIncomePeriodButton("90 days", IncomePeriod.Days90);
        ImGui.SameLine(0, 3f * ImGuiHelpers.GlobalScale);
        DrawIncomePeriodButton("1 year", IncomePeriod.Days365);
        ImGui.SameLine(0, 3f * ImGuiHelpers.GlobalScale);
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
        string[] labels = ["Gross gil", "Gil / day", "Gil / voyage", "FC name"];
        var value = this.configuration.IncomeSort;
        if (DrawEnumCombo("##income-sort", labels, ref value))
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
