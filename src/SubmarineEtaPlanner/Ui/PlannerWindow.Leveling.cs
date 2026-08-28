using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using SubmarineEtaPlanner.Planner;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

public sealed partial class PlannerWindow
{
    private void DrawLevelingPage()
    {
        var currentSnapshot = EnsureFleetSnapshot();
        if (currentSnapshot is null)
            return;

        DrawFleetNotices(currentSnapshot);
        DrawSearch("Search all leveling fleets…");
        ImGui.SameLine();
        DrawLevelingFilterCombo();
        ImGui.SameLine();
        DrawLevelingSortCombo();

        var now = DateTimeOffset.UtcNow;
        var projections = CreateProjections(currentSnapshot, now)
            .Where(projection => projection.RoleSummary.HasLeveling)
            .Where(projection => MatchesSearch(projection.State))
            .Where(projection => this.configuration.LevelingFilter switch
            {
                LevelingFilter.Actionable => projection.ImmediateActionCount > 0,
                LevelingFilter.Favorites => IsFavorite(projection),
                _ => true,
            })
            .OrderByDescending(IsFavorite)
            .ThenBy(projection => this.configuration.LevelingSort switch
            {
                LevelingSort.LowestRank => projection.Submarines.Min(submarine => submarine.Rank),
                _ => 0,
            })
            .ThenBy(projection => this.configuration.LevelingSort switch
            {
                LevelingSort.FarmReadyEta => projection.CompletionP50AtUtc ?? DateTimeOffset.MaxValue,
                LevelingSort.NextAction => projection.Submarines.Select(submarine => submarine.NextActionAtUtc).Where(value => value is not null).Min() ?? DateTimeOffset.MaxValue,
                _ => DateTimeOffset.MinValue,
            })
            .ThenBy(projection => projection.State.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ImGui.Spacing();
        ImGui.TextColored(PlannerUi.Muted, $"{projections.Length} leveling fleet{(projections.Length == 1 ? string.Empty : "s")} · every submarine remains visible when expanded");
        ImGui.Spacing();
        var headerContexts = projections.ToDictionary(
            projection => projection.State.FcIdKey,
            projection => new OperationsHeaderRenderContext(
                OperationsFcHeaderPresentation.Create(projection, IsFavorite(projection), now),
                CurrentVoyageProgressFormatter.CreateForFc(projection.State.Submarines, this.catalog, now)));
        var headerLayout = CalculateOperationsHeaderLayout(
            headerContexts.Values.Select(context => context.Presentation),
            ImGui.GetContentRegionAvail().X);
        DrawOperationsHeaderLegend(headerLayout);
        foreach (var projection in projections)
            DrawLevelingFleetGroup(projection, now, headerContexts[projection.State.FcIdKey], headerLayout);
    }

    private void DrawLevelingFleetGroup(
        FcOperationalProjection projection,
        DateTimeOffset now,
        OperationsHeaderRenderContext headerContext,
        OperationsHeaderLayout layout)
    {
        if (this.viewState.ExpansionOverride is { } expansion)
            ImGui.SetNextItemOpen(expansion, ImGuiCond.Always);

        ImGui.Spacing();
        var open = DrawAlignedOperationsHeader(
            $"leveling-fc-{projection.State.FcIdKey}",
            headerContext,
            layout);
        DrawOperationsHeaderTooltip(projection, headerContext, now);
        if (!open)
            return;

        var completion = OperationsCompletionPresentation.Create(projection);
        var voyages = projection.Submarines.Sum(submarine => submarine.VoyagesRemaining);
        var bottleneck = projection.Submarines
            .Where(submarine => submarine.TargetEtaAtUtc is not null)
            .OrderByDescending(submarine => submarine.TargetEtaAtUtc)
            .FirstOrDefault();
        var levelingDetails = $"{completion.Label} · {voyages} voyage{(voyages == 1 ? string.Empty : "s")} remaining" +
                              (bottleneck is null ? string.Empty : $" · Bottleneck: {bottleneck.Name}");
        ImGui.TextColored(PlannerUi.Muted, levelingDetails);
        PlannerUi.Tooltip(completion.Tooltip);
        ImGui.Spacing();
        DrawLevelingSubmarineTable(projection, now);
        DrawExpandedLevelingForecasts(projection, now);
    }

    private void DrawLevelingSubmarineTable(FcOperationalProjection projection, DateTimeOffset now)
    {
        var layout = CalculateResponsiveTableLayout(
            ImGui.GetContentRegionAvail().X,
            new ResponsiveTableColumn("Submarine", projection.Submarines.Select(submarine => submarine.Name), 125, 230),
            new ResponsiveTableColumn("Rank", projection.Submarines.Select(submarine => OperationsRankPresentation.Create(submarine).Label), 105, 165),
            new ResponsiveTableColumn("Build", projection.Submarines.Select(submarine => submarine.CurrentBuild.Code), 72, 100),
            new ResponsiveTableColumn("State", projection.Submarines.Select(submarine => CompactOperationalStatePresentation.Create(submarine, now).Label), 115, 190),
            new ResponsiveTableColumn("Current / next route", projection.Submarines.Select(submarine => FormatCompactRoute(submarine.DisplayedRoute)), 170, 420, Flexible: true, FlexWeight: 1.5f, FillRemaining: true),
            new ResponsiveTableColumn("Purpose", projection.Submarines.Select(submarine => submarine.RoutePurpose.ToString()), 82, 145),
            new ResponsiveTableColumn("Expected EXP", projection.Submarines.Select(submarine => submarine.ExpectedExp?.ToString("N0") ?? "Unavailable"), 105, 145),
            new ResponsiveTableColumn("Target ETA", projection.Submarines.Select(submarine => submarine.Rank >= submarine.EffectiveTargetRank ? "Ready" : submarine.TargetEtaAtUtc is { } eta ? FormatRelative(eta, now) : "Unavailable"), 105, 155));
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit;
        if (layout.RequiresHorizontalScroll)
            flags |= ImGuiTableFlags.ScrollX;
        var tableHeight = CalculateTableHeight(projection.Submarines.Count, layout.RequiresHorizontalScroll);
        if (!ImGui.BeginTable(
                $"leveling-projection-table-{projection.State.FcIdKey}",
                8,
                flags,
                new Vector2(-1, tableHeight),
                layout.RequiresHorizontalScroll ? layout.InnerWidth : 0f))
            return;

        SetupResponsiveTableColumns(layout);
        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableHeadersRow();
        foreach (var submarine in projection.Submarines)
        {
            var expansionKey = GetLevelingForecastExpansionKey(projection.State.FcIdKey, submarine.SubmarineId);
            var expanded = this.expandedSubmarines.Contains(expansionKey);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var rowStart = ImGui.GetCursorScreenPos();
            var clicked = ImGui.Selectable(
                $"##leveling-submarine-{projection.State.FcIdKey}-{submarine.SubmarineId}",
                false,
                ImGuiSelectableFlags.SpanAllColumns,
                new Vector2(0, ImGui.GetFrameHeight()));
            var rowHovered = ImGui.IsItemHovered();
            var rowEnd = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(rowStart + new Vector2(3f * ImGuiHelpers.GlobalScale, 1f * ImGuiHelpers.GlobalScale));
            PlannerUi.Icon(expanded ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight, PlannerUi.Teal);
            ImGui.SameLine();
            ImGui.TextUnformatted(submarine.Name);
            ImGui.SetCursorScreenPos(rowEnd);
            if (clicked)
            {
                if (!this.expandedSubmarines.Add(expansionKey))
                    this.expandedSubmarines.Remove(expansionKey);
            }
            if (rowHovered)
                ImGui.SetTooltip(expanded ? "Hide voyage forecast" : "Show complete voyage forecast");

            ImGui.TableNextColumn();
            var rankPresentation = OperationsRankPresentation.Create(submarine);
            ImGui.TextUnformatted(rankPresentation.Label);
            if (rankPresentation.Tooltip is not null)
                PlannerUi.Tooltip(rankPresentation.Tooltip);
            ImGui.TableNextColumn();
            DrawCurrentBuild(submarine.CurrentBuild);
            ImGui.TableNextColumn();
            var compactState = CompactOperationalStatePresentation.Create(submarine, now);
            ImGui.TextUnformatted(compactState.Label);
            PlannerUi.Tooltip(compactState.Tooltip);
            ImGui.TableNextColumn();
            DrawCompactRoute(submarine.DisplayedRoute);
            if ((submarine.State is OperationalState.Underway or OperationalState.ReadyToCollect) &&
                submarine.Rank < submarine.EffectiveTargetRank &&
                submarine.RecommendedNextRoute.Count > 0)
            {
                ImGui.SameLine();
                ImGui.TextColored(PlannerUi.Muted, "then");
                ImGui.SameLine();
                DrawCompactRoute(submarine.RecommendedNextRoute, PlannerUi.Teal);
            }
            if (submarine.AlternativeRoutes.Count > 1 && ImGui.IsItemHovered())
                PlannerUi.Tooltip("Conditional recommendation: alternative routes remain possible depending on unlock outcomes.");
            DrawTableText(submarine.RoutePurpose.ToString());
            DrawTableText(submarine.ExpectedExp is { } exp ? exp.ToString("N0") : "Unavailable");
            if (submarine.ExpectedExp is null && submarine.ProjectionUnavailableReason is not null)
                PlannerUi.Tooltip(submarine.ProjectionUnavailableReason);
            DrawTableText(submarine.Rank >= submarine.EffectiveTargetRank
                ? "Ready"
                : submarine.TargetEtaAtUtc is { } eta ? FormatRelative(eta, now) : "Unavailable");
        }
        ImGui.EndTable();
    }

    private void DrawExpandedLevelingForecasts(FcOperationalProjection projection, DateTimeOffset now)
    {
        foreach (var submarine in projection.Submarines)
        {
            var expansionKey = GetLevelingForecastExpansionKey(projection.State.FcIdKey, submarine.SubmarineId);
            if (!this.expandedSubmarines.Contains(expansionKey))
                continue;

            ImGui.PushID(expansionKey);
            ImGui.Indent(12f * ImGuiHelpers.GlobalScale);
            ImGui.Spacing();
            PlannerUi.IconText(FontAwesomeIcon.Ship, $"{submarine.Name} voyage forecast", PlannerUi.Teal);
            var result = projection.Result?.PerSubResults.FirstOrDefault(item => item.SubmarineId == submarine.SubmarineId);
            if (result is null)
            {
                PlannerUi.Callout(
                    "forecast-unavailable",
                    FontAwesomeIcon.ExclamationTriangle,
                    "Forecast unavailable",
                    submarine.ProjectionUnavailableReason ?? "No modeled voyage forecast is available for this submarine.",
                    PlannerUi.Amber);
            }
            else
            {
                DrawSubDetails(result, this.configuration.Settings.ShowRouteDiagnostics, now);
            }
            ImGui.Unindent(12f * ImGuiHelpers.GlobalScale);
            ImGui.PopID();
        }
    }

    private static string GetLevelingForecastExpansionKey(string fcIdKey, long submarineId)
        => $"leveling:{fcIdKey}:{submarineId}";

    private void DrawLevelingSortCombo()
    {
        string[] labels = ["Farm-ready ETA", "Lowest rank", "Next action", "FC name"];
        var value = this.configuration.LevelingSort;
        if (DrawEnumCombo("##leveling-sort", labels, ref value))
        {
            this.configuration.LevelingSort = value;
            this.saveConfiguration();
        }
    }

    private void DrawLevelingFilterCombo()
    {
        string[] labels = ["All leveling fleets", "Actionable submarines", "Favorites"];
        var value = this.configuration.LevelingFilter;
        if (DrawEnumCombo("##leveling-filter", labels, ref value))
        {
            this.configuration.LevelingFilter = value;
            this.saveConfiguration();
        }
    }
}
