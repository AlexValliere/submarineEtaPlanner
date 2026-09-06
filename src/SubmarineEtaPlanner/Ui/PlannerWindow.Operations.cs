using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using SubmarineEtaPlanner.Planner;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

public sealed partial class PlannerWindow
{
    private OperationsAttentionFilter attentionFilter;

    private void DrawOperationsPage()
    {
        var currentSnapshot = EnsureFleetSnapshot();
        if (currentSnapshot is null) return;
        DrawFleetNotices(currentSnapshot);
        DrawSearch("Search FC, world, or submarine…");
        PlannerUi.SameLineIfFits("All fleets");
        DrawOperationsViewButton("All fleets", OperationsView.AllFleets);
        PlannerUi.SameLineIfFits("Leveling");
        DrawOperationsViewButton("Leveling", OperationsView.Leveling);
        PlannerUi.SameLineIfFits("Farming");
        DrawOperationsViewButton("Farming", OperationsView.Farming);
        PlannerUi.SameLineIfFits("", 200f * ImGuiHelpers.GlobalScale);
        DrawOperationsSortCombo();

        var now = DateTimeOffset.UtcNow;
        var all = CreateProjections(currentSnapshot, now);
        var requiredMode = this.configuration.OperationsView switch
        {
            OperationsView.Leveling => FleetMode.Leveling,
            OperationsView.Farming => FleetMode.Farming,
            _ => (FleetMode?)null,
        };
        var matched = all.Where(fc => MatchesSearch(fc.State))
            .Where(fc => FleetPresentationFiltering.Includes(fc, requiredMode)).ToArray();
        var fuel = matched.ToDictionary(fc => fc.State.FcIdKey, fc => GetFuelPresentation(fc, now));
        DrawAttentionCounters(OperationsAttentionSummary.Create(matched, fuel, now, TimeZoneInfo.Local));
        var filtered = matched.Where(fc => OperationsAttentionSummary.MatchesFleet(
            fc, fuel[fc.State.FcIdKey], this.attentionFilter, now, TimeZoneInfo.Local)).ToArray();
        var fleets = this.configuration.OperationsSort switch
        {
            OperationsSort.FarmReadyEta => FleetPresentationOrdering.FarmReadyEta(filtered, IsFavorite),
            OperationsSort.FcName => FleetPresentationOrdering.ByName(filtered, IsFavorite),
            _ => FleetPresentationOrdering.ActionsFirst(filtered, IsFavorite),
        };
        PlannerUi.WrappedText($"{fleets.Count} fleets shown of {all.Count} tracked", PlannerUi.Muted);
        if (fleets.Count == 0)
        {
            PlannerUi.WrappedText("No fleets match these filters.");
            if (ImGui.Button("Clear filters"))
            {
                this.attentionFilter = OperationsAttentionFilter.None;
                this.fcSearch = string.Empty;
                this.configuration.OperationsView = OperationsView.AllFleets;
                this.saveConfiguration();
            }
        }
        foreach (var fc in fleets) DrawOperationsFleetGroup(fc, fuel[fc.State.FcIdKey], now);
    }

    private void DrawAttentionCounters(OperationsAttentionSummary counts)
    {
        ImGui.Spacing();
        var items = new[]
        {
            (OperationsAttentionFilter.Collect, $"Ready to collect: {counts.Collect} subs"),
            (OperationsAttentionFilter.ReturningToday, $"Returning today: {counts.ReturningToday} subs"),
            (OperationsAttentionFilter.LowFuel, $"Low fuel: {counts.LowFuel} FCs"),
            (OperationsAttentionFilter.NeedsSetup, $"Needs setup: {counts.NeedsSetup} FCs"),
        };
        var first = true;
        foreach (var (filter, label) in items)
        {
            if (!first) PlannerUi.SameLineIfFits(label);
            if (PlannerUi.SegmentedButton($"attention-{filter}", label, this.attentionFilter == filter))
                this.attentionFilter = this.attentionFilter == filter ? OperationsAttentionFilter.None : filter;
            first = false;
        }
        if (this.attentionFilter != OperationsAttentionFilter.None)
        {
            PlannerUi.SameLineIfFits("Clear attention filter");
            if (ImGui.SmallButton("Clear attention filter")) this.attentionFilter = OperationsAttentionFilter.None;
        }
        ImGui.Spacing();
    }

    private void DrawOperationsFleetGroup(FcOperationalProjection fc, FleetFuelPresentation fuel, DateTimeOffset now)
    {
        ImGui.Spacing();
        DrawFavoriteControl(fc.State.FcIdKey);
        if (this.viewState.ExpansionOverride is { } expansion) ImGui.SetNextItemOpen(expansion, ImGuiCond.Always);
        var presentation = OperationsFcHeaderPresentation.Create(fc, false, now);
        var progress = CurrentVoyageProgressFormatter.CreateForFc(fc.State.Submarines, this.catalog, now);
        var open = DrawCompactOperationsHeader(fc, presentation, progress, fuel);
        if (!open) return;
        DrawFcShortcuts(fc.State.FcIdKey);
        PlannerUi.WrappedText(OperationsCompletionPresentation.Create(fc).Label, PlannerUi.Muted);
        DrawFuelRunway(fc, now);
        ImGui.Spacing();
        var narrow = ImGui.GetContentRegionAvail().X < 680f * ImGuiHelpers.GlobalScale;
        if (BeginOperationsTable($"operations-legend-{fc.State.FcIdKey}", narrow))
        {
            ImGui.TableHeadersRow();
            ImGui.EndTable();
        }
        foreach (var submarine in fc.Submarines)
            DrawOperationsEntry(fc, submarine, fuel, now, narrow);
    }

    private bool DrawCompactOperationsHeader(FcOperationalProjection fc, OperationsFcHeaderPresentation presentation,
        FcCurrentVoyageProgressPresentation progress, FleetFuelPresentation fuel)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var scale = ImGuiHelpers.GlobalScale;
        var twoLines = width < 760f * scale;
        var line = ImGui.GetTextLineHeight();
        var height = twoLines ? line * 2 + 12f * scale : ImGui.GetFrameHeight();
        DrawFcProgressBackground(progress, height);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(ImGui.GetStyle().FramePadding.X, (height - line) / 2));
        ImGui.PushStyleColor(ImGuiCol.Header, Vector4.Zero);
        var open = ImGui.CollapsingHeader($"###operations-fc-{fc.State.FcIdKey}");
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        var text = new[] { fc.State.DisplayName, presentation.Mode, presentation.Attention, CompactFuelLabel(fuel, true) };
        var contentWidth = Math.Max(1, width - 26f * scale);
        var fractions = twoLines ? new[] { .6f, .4f, .5f, .5f } : new[] { .32f, .18f, .27f, .23f };
        var x = origin.X + 26f * scale;
        for (var i = 0; i < text.Length; i++)
        {
            if (twoLines && i == 2) x = origin.X + 26f * scale;
            var y = origin.Y + 5f * scale + (twoLines && i >= 2 ? line + 2f * scale : 0);
            var cellWidth = contentWidth * fractions[i];
            var color = i == 3 ? FuelStatusColor(fuel) : i == 2 && presentation.HasImmediateActions ? PlannerUi.Amber : ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
            ImGui.GetWindowDrawList().AddText(new Vector2(x, y), ImGui.ColorConvertFloat4ToU32(color),
                FitHeaderText(text[i], Math.Max(1, cellWidth - 8f * scale)));
            x += cellWidth;
        }
        PlannerUi.Tooltip(string.Join("\n", text) + (fuel.HasFarming ? "\n" + CompactFuelLabel(fuel) : ""));
        return open;
    }

    private static bool BeginOperationsTable(string id, bool narrow)
    {
        if (!ImGui.BeginTable(id, narrow ? 3 : 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
            return false;
        ImGui.TableSetupColumn("Submarine", ImGuiTableColumnFlags.WidthStretch, narrow ? 1.5f : 1.25f);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch, .9f);
        ImGui.TableSetupColumn("Return time", ImGuiTableColumnFlags.WidthStretch, 1.1f);
        if (!narrow)
        {
            ImGui.TableSetupColumn("Route", ImGuiTableColumnFlags.WidthStretch, 1.7f);
            ImGui.TableSetupColumn("Next action", ImGuiTableColumnFlags.WidthStretch, 1.65f);
        }
        return true;
    }

    private void DrawOperationsEntry(FcOperationalProjection fc, SubmarineOperationalProjection submarine,
        FleetFuelPresentation fuel, DateTimeOffset now, bool narrow)
    {
        var tracked = fc.State.Submarines.First(sub => sub.SubmarineId == submarine.SubmarineId);
        var plan = fuel.Routes.FirstOrDefault(route => route.SubmarineId == submarine.SubmarineId);
        var row = CompactSubmarinePresentation.Create(submarine, tracked, plan);
        var key = $"operations:{fc.State.FcIdKey}:{submarine.SubmarineId}";
        var expanded = this.expandedSubmarines.Contains(key);
        var highlighted = OperationsAttentionSummary.MatchesSubmarine(submarine, fc.State, this.attentionFilter, now, TimeZoneInfo.Local) ||
            (this.attentionFilter == OperationsAttentionFilter.LowFuel && submarine.EffectiveRole == EffectiveSubmarineRole.Farming && fuel.LowFuel) ||
            (this.attentionFilter == OperationsAttentionFilter.NeedsSetup && submarine.EffectiveRole == EffectiveSubmarineRole.Farming &&
                (plan?.IsUsable != true || !fuel.Stock.IsAvailable));
        if (BeginOperationsTable(key, narrow))
        {
            ImGui.TableNextRow();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.ColorConvertFloat4ToU32(
                highlighted ? new Vector4(.08f, .28f, .30f, .6f) : PlannerUi.PanelBackground with { W = .5f }));
            ImGui.TableNextColumn();
            var start = ImGui.GetCursorScreenPos();
            var name = $"{(expanded ? "▾" : "▸")} {submarine.Name}";
            var available = Math.Max(1f, ImGui.GetContentRegionAvail().X);
            var nameHeight = Math.Max(ImGui.GetTextLineHeight(), ImGui.CalcTextSize(name, false, available).Y);
            if (ImGui.Selectable($"##expand-{key}", false, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0, nameHeight)))
            {
                if (!this.expandedSubmarines.Add(key)) this.expandedSubmarines.Remove(key);
                expanded = !expanded;
            }
            PlannerUi.Tooltip(expanded ? "Hide submarine details" : "Show submarine details");
            var after = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(start);
            PlannerUi.WrappedText(name);
            ImGui.SetCursorScreenPos(after);
            ImGui.TableNextColumn(); PlannerUi.WrappedText(row.Status);
            ImGui.TableNextColumn();
            PlannerUi.WrappedText(submarine.State == OperationalState.Idle ? "—" :
                tracked.ReturnAtUtc > now ? $"In {FormatDuration(tracked.ReturnAtUtc - now)}" :
                submarine.State == OperationalState.ReadyToCollect ? "Ready" : "Awaiting tracker");
            if (tracked.ReturnAtUtc != DateTimeOffset.MinValue) PlannerUi.Tooltip($"Return: {tracked.ReturnAtUtc.LocalDateTime:g}");
            if (!narrow)
            {
                ImGui.TableNextColumn(); DrawOperationsRoutes(row);
                ImGui.TableNextColumn(); DrawOperationsAction(row);
            }
            ImGui.EndTable();
        }
        if (narrow)
        {
            DrawOperationsRoutes(row);
            DrawOperationsAction(row);
        }
        if (expanded)
        {
            ImGui.Indent(8f * ImGuiHelpers.GlobalScale);
            PlannerUi.WrappedText($"Rank {submarine.Rank} · Build {submarine.CurrentBuild.Code} · Purpose {submarine.RoutePurpose}");
            PlannerUi.WrappedText($"Expected EXP: {(submarine.ExpectedExp is { } exp ? exp.ToString("N0") : "Unavailable")}");
            var target = submarine.IsTargetComplete ? "Target reached" : submarine.TargetEtaAtUtc is { } eta
                ? eta.LocalDateTime.ToString("g") : "Unavailable";
            PlannerUi.WrappedText($"Target R{submarine.EffectiveTargetRank}: {target}");
            var result = fc.Result?.PerSubResults.FirstOrDefault(sub => sub.SubmarineId == submarine.SubmarineId);
            if (result?.EtaForecast is { } range)
                PlannerUi.WrappedText($"Likely range: {range.P10AtUtc.LocalDateTime:g} – {range.P90AtUtc.LocalDateTime:g}", PlannerUi.Muted);
            foreach (var reason in new[] { submarine.CurrentBuild.UnavailableReason, row.Reason, result?.IncompleteReason }
                .Where(reason => !string.IsNullOrWhiteSpace(reason)).Distinct())
                PlannerUi.WrappedText(reason!, PlannerUi.Amber);
            if (submarine.AlternativeRoutes.Count > 1)
                PlannerUi.WrappedText("Conditional recommendation: the next route depends on sector discovery outcomes.", PlannerUi.Muted);
            ImGui.Unindent(8f * ImGuiHelpers.GlobalScale);
        }
        ImGui.Spacing();
    }

    private void DrawOperationsRoutes(CompactSubmarinePresentation row)
    {
        PlannerUi.WrappedText($"Current: {(row.CurrentRoute.Count > 0 ? FormatCompactRoute(row.CurrentRoute) : "—")}");
        if (row.NextRoute.Count > 0)
            PlannerUi.WrappedText($"{row.NextRouteLabel}: {FormatCompactRoute(row.NextRoute)}", PlannerUi.Muted);
    }

    private static void DrawOperationsAction(CompactSubmarinePresentation row)
    {
        PlannerUi.WrappedText(row.ActionLabel);
        PlannerUi.Tooltip(RecommendedActionFormatter.Format(row.Action) +
            (row.Reason is null ? "" : "\n" + row.Reason) + "\nPlanning guidance; perform workshop actions in game.");
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
        ImGui.SetNextItemWidth(Math.Min(200f * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        var value = this.configuration.OperationsSort;
        if (DrawEnumCombo("##operations-sort", labels, ref value, 200f))
        {
            this.configuration.OperationsSort = value;
            this.saveConfiguration();
        }
    }
}
