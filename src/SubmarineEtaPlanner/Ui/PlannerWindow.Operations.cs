using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using SubmarineEtaPlanner.Planner;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

public sealed partial class PlannerWindow
{
    private void DrawOperationsPage()
    {
        var currentSnapshot = EnsureFleetSnapshot();
        if (currentSnapshot is null)
            return;

        DrawFleetNotices(currentSnapshot);
        DrawSearch("Search FC, world, or submarine…");
        ImGui.SameLine();
        DrawOperationsViewButton("All fleets", OperationsView.AllFleets);
        ImGui.SameLine(0, 3f * ImGuiHelpers.GlobalScale);
        DrawOperationsViewButton("Leveling", OperationsView.Leveling);
        ImGui.SameLine(0, 3f * ImGuiHelpers.GlobalScale);
        DrawOperationsViewButton("Farming", OperationsView.Farming);
        ImGui.SameLine();
        DrawOperationsSortCombo();

        var now = DateTimeOffset.UtcNow;
        var allProjections = CreateProjections(currentSnapshot, now);
        var requiredMode = this.configuration.OperationsView switch
        {
            OperationsView.Leveling => FleetMode.Leveling,
            OperationsView.Farming => FleetMode.Farming,
            _ => (FleetMode?)null,
        };
        var filteredProjections = allProjections
            .Where(projection => MatchesSearch(projection.State))
            .Where(projection => FleetPresentationFiltering.Includes(projection, requiredMode))
            .ToArray();
        var projections = this.configuration.OperationsSort switch
        {
            OperationsSort.FarmReadyEta => FleetPresentationOrdering.FarmReadyEta(filteredProjections, IsFavorite),
            OperationsSort.FcName => FleetPresentationOrdering.ByName(filteredProjections, IsFavorite),
            _ => FleetPresentationOrdering.ActionsFirst(filteredProjections, IsFavorite),
        };

        ImGui.Spacing();
        ImGui.TextColored(PlannerUi.Muted, $"{projections.Count} fleet{(projections.Count == 1 ? string.Empty : "s")} shown of {allProjections.Count} tracked");
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
            DrawOperationsFleetGroup(projection, now, headerContexts[projection.State.FcIdKey], headerLayout);
    }

    private void DrawOperationsFleetGroup(
        FcOperationalProjection projection,
        DateTimeOffset now,
        OperationsHeaderRenderContext headerContext,
        OperationsHeaderLayout layout)
    {
        if (this.viewState.ExpansionOverride is { } expansion)
            ImGui.SetNextItemOpen(expansion, ImGuiCond.Always);

        ImGui.Spacing();
        var open = DrawAlignedOperationsHeader(
            $"operations-fc-{projection.State.FcIdKey}",
            headerContext,
            layout);
        DrawOperationsHeaderTooltip(projection, headerContext, now);
        if (!open)
            return;

        var completion = OperationsCompletionPresentation.Create(projection);
        ImGui.TextColored(PlannerUi.Muted, completion.Label);
        PlannerUi.Tooltip(completion.Tooltip);
        ImGui.Spacing();
        DrawFuelRunway(projection, now);
        ImGui.Spacing();
        DrawOperationsSubmarineTable(projection, now);
    }

    private void DrawOperationsSubmarineTable(FcOperationalProjection projection, DateTimeOffset now)
    {
        var layout = CalculateResponsiveTableLayout(
            ImGui.GetContentRegionAvail().X,
            new ResponsiveTableColumn("Submarine", projection.Submarines.Select(submarine => submarine.Name), 115, 220),
            new ResponsiveTableColumn("Rank → after voyage", projection.Submarines.Select(submarine => OperationsRankPresentation.Create(submarine).Label), 125, 165),
            new ResponsiveTableColumn("Build", projection.Submarines.Select(submarine => submarine.CurrentBuild.Code), 72, 100),
            new ResponsiveTableColumn("State", projection.Submarines.Select(submarine => CompactOperationalStatePresentation.Create(submarine, now).Label), 115, 190),
            new ResponsiveTableColumn("Current / next route", projection.Submarines.Select(submarine => FormatCompactRoute(submarine.DisplayedRoute)), 170, 420, Flexible: true, FlexWeight: 1.5f),
            new ResponsiveTableColumn("Purpose", projection.Submarines.Select(submarine => submarine.RoutePurpose.ToString()), 82, 145),
            new ResponsiveTableColumn("Expected EXP", projection.Submarines.Select(submarine => submarine.ExpectedExp?.ToString("N0") ?? "Unavailable"), 105, 145),
            new ResponsiveTableColumn("Target ETA", projection.Submarines.Select(submarine => submarine.Rank >= submarine.EffectiveTargetRank ? "Ready" : submarine.TargetEtaAtUtc is { } eta ? FormatRelative(eta, now) : "Unavailable"), 105, 155));
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit;
        if (layout.RequiresHorizontalScroll)
            flags |= ImGuiTableFlags.ScrollX;
        var tableHeight = CalculateTableHeight(projection.Submarines.Count, layout.RequiresHorizontalScroll);
        if (!ImGui.BeginTable(
                $"operations-projection-table-{projection.State.FcIdKey}",
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
            ImGui.TableNextRow();
            DrawTableText(submarine.Name);
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

    private void DrawOperationsViewButton(string label, OperationsView view)
    {
        if (PlannerUi.SegmentedButton($"operations-view-{view}", label, this.configuration.OperationsView == view))
        {
            this.configuration.OperationsView = view;
            this.saveConfiguration();
        }
    }

    private void DrawOperationsSortCombo()
    {
        string[] labels = ["Next return · actions first", "Farm-ready ETA", "FC name"];
        var value = this.configuration.OperationsSort;
        if (DrawEnumCombo("##operations-sort", labels, ref value))
        {
            this.configuration.OperationsSort = value;
            this.saveConfiguration();
        }
    }
}
