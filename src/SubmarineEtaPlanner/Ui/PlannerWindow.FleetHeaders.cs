using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using SubmarineEtaPlanner.Planner;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

public sealed partial class PlannerWindow
{
    private sealed record OperationsHeaderRenderContext(
        OperationsFcHeaderPresentation Presentation,
        FcCurrentVoyageProgressPresentation CurrentVoyages);

    private readonly record struct OperationsHeaderColumn(float Offset, float Width, int Line);

    private sealed record OperationsHeaderLayout(
        bool TwoLine,
        float HeaderHeight,
        float LegendHeight,
        OperationsHeaderColumn FreeCompany,
        OperationsHeaderColumn World,
        OperationsHeaderColumn Mode,
        OperationsHeaderColumn Attention,
        OperationsHeaderColumn FarmReady,
        OperationsHeaderColumn Ranks);

    private readonly record struct CompactFcHeaderColumn(float Offset, float Width, int Line);

    private sealed record CompactFcHeaderLayout(
        float HeaderHeight, int LineCount, IReadOnlyList<CompactFcHeaderColumn> Columns);

    // A page calculates these boundaries once; its legend and every FC use the same layout.
    private static CompactFcHeaderLayout CalculateCompactFcHeaderLayout(
        IReadOnlyList<float> desiredWidths, IReadOnlyList<float> stretchWeights,
        int wrapAfter, float availableWidth)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var gutter = 26f * scale;
        var gap = 14f * scale;
        var contentWidth = Math.Max(1f, availableWidth - gutter - 10f * scale);
        var twoLines = desiredWidths.Sum() + gap * (desiredWidths.Count - 1) > contentWidth;
        var columns = new CompactFcHeaderColumn[desiredWidths.Count];

        void PlaceLine(int start, int end, int line)
        {
            var width = Math.Max(1f, contentWidth - gap * (end - start - 1));
            var desired = Enumerable.Range(start, end - start).Sum(index => desiredWidths[index]);
            var weight = Enumerable.Range(start, end - start).Sum(index => stretchWeights[index]);
            var offset = gutter;
            for (var index = start; index < end; index++)
            {
                var columnWidth = width < desired
                    ? desiredWidths[index] * width / Math.Max(1f, desired)
                    : desiredWidths[index] + (width - desired) * stretchWeights[index] / Math.Max(1f, weight);
                columns[index] = new(offset, columnWidth, line);
                offset += columnWidth + gap;
            }
        }

        if (twoLines)
        {
            PlaceLine(0, wrapAfter, 0);
            PlaceLine(wrapAfter, desiredWidths.Count, 1);
        }
        else PlaceLine(0, desiredWidths.Count, 0);
        return new(
            twoLines ? ImGui.GetTextLineHeight() * 2 + 14f * scale : ImGui.GetFrameHeight(),
            twoLines ? 2 : 1, columns);
    }

    private static void DrawCompactFcHeaderCell(
        Vector2 origin, CompactFcHeaderLayout layout, int index, string text,
        Vector4 color, bool rightAligned = false)
    {
        var column = layout.Columns[index];
        var scale = ImGuiHelpers.GlobalScale;
        var lineHeight = ImGui.GetTextLineHeight();
        var lineGap = 2f * scale;
        var contentHeight = lineHeight * layout.LineCount + lineGap * (layout.LineCount - 1);
        var y = origin.Y + (layout.HeaderHeight - contentHeight) / 2
            + column.Line * (lineHeight + lineGap);
        var padding = 4f * scale;
        var fitted = FitHeaderText(text, Math.Max(1f, column.Width - padding * 2));
        var x = origin.X + column.Offset + (rightAligned
            ? Math.Max(padding, column.Width - padding - ImGui.CalcTextSize(fitted).X) : padding);
        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(new(origin.X + column.Offset, y),
            new(origin.X + column.Offset + column.Width, y + lineHeight), true);
        drawList.AddText(new(x, y), ImGui.ColorConvertFloat4ToU32(color), fitted);
        drawList.PopClipRect();
    }

    private static void DrawCompactFcHeaderLegend(
        CompactFcHeaderLayout layout, IReadOnlyList<string> labels, int rightAlignFrom = int.MaxValue)
    {
        var origin = ImGui.GetCursorScreenPos() + new Vector2(FavoriteControlWidth, 0);
        for (var index = 0; index < labels.Count; index++)
            DrawCompactFcHeaderCell(origin, layout, index, labels[index], PlannerUi.Muted, index >= rightAlignFrom);
        ImGui.Dummy(new(ImGui.GetContentRegionAvail().X, layout.HeaderHeight));
    }

    private static OperationsHeaderLayout CalculateOperationsHeaderLayout(
        IEnumerable<OperationsFcHeaderPresentation> presentations,
        float availableWidth)
    {
        var values = presentations.ToArray();
        var scale = ImGuiHelpers.GlobalScale;
        var gap = 10f * scale;
        var gutter = 40f * scale;
        var fcWidth = MeasureHeaderColumn(values.Select(value => value.FreeCompany), "FC", 90f, 155f);
        var worldWidth = MeasureHeaderColumn(values.Select(value => value.World), "World", 90f, 150f);
        var modeWidth = MeasureHeaderColumn(values.Select(value => value.Mode), "Mode", 82f, 105f);
        var attentionWidth = MeasureHeaderColumn(values.Select(value => value.Attention), "Next action / return", 125f, 185f);
        var farmReadyWidth = MeasureHeaderColumn(values.Select(value => value.FarmReady), "Farm ready", 105f, 155f);
        var ranksWidth = MeasureHeaderColumn(values.Select(value => value.Ranks), "Ranks", 170f, 275f);
        var singleLineRequired = gutter + fcWidth + worldWidth + modeWidth + attentionWidth + farmReadyWidth + ranksWidth + (gap * 5f);
        var lineHeight = ImGui.GetTextLineHeight();

        if (availableWidth >= singleLineRequired)
        {
            var fc = new OperationsHeaderColumn(gutter, fcWidth, 0);
            var world = new OperationsHeaderColumn(fc.Offset + fc.Width + gap, worldWidth, 0);
            var mode = new OperationsHeaderColumn(world.Offset + world.Width + gap, modeWidth, 0);
            var attention = new OperationsHeaderColumn(mode.Offset + mode.Width + gap, attentionWidth, 0);
            var farmReady = new OperationsHeaderColumn(attention.Offset + attention.Width + gap, farmReadyWidth, 0);
            var ranksOffset = farmReady.Offset + farmReady.Width + gap;
            return new OperationsHeaderLayout(
                false,
                ImGui.GetFrameHeight(),
                ImGui.GetFrameHeight(),
                fc,
                world,
                mode,
                attention,
                farmReady,
                new OperationsHeaderColumn(ranksOffset, Math.Max(1f, availableWidth - ranksOffset), 0));
        }

        var contentWidth = Math.Max(1f, availableWidth - gutter);
        var firstLineWidth = Math.Max(1f, contentWidth - (gap * 3f));
        var fcTwoLine = Math.Max(82f * scale, firstLineWidth * 0.24f);
        var worldTwoLine = Math.Max(82f * scale, firstLineWidth * 0.22f);
        var modeTwoLine = Math.Max(78f * scale, firstLineWidth * 0.17f);
        var attentionTwoLine = Math.Max(1f, firstLineWidth - fcTwoLine - worldTwoLine - modeTwoLine);
        var secondLineWidth = Math.Max(1f, contentWidth - gap);
        var farmReadyTwoLine = Math.Max(110f * scale, secondLineWidth * 0.30f);
        var ranksTwoLine = Math.Max(1f, secondLineWidth - farmReadyTwoLine);
        var height = (lineHeight * 2f) + (14f * scale);
        return new OperationsHeaderLayout(
            true,
            height,
            height,
            new OperationsHeaderColumn(gutter, fcTwoLine, 0),
            new OperationsHeaderColumn(gutter + fcTwoLine + gap, worldTwoLine, 0),
            new OperationsHeaderColumn(gutter + fcTwoLine + gap + worldTwoLine + gap, modeTwoLine, 0),
            new OperationsHeaderColumn(gutter + fcTwoLine + gap + worldTwoLine + gap + modeTwoLine + gap, attentionTwoLine, 0),
            new OperationsHeaderColumn(gutter, farmReadyTwoLine, 1),
            new OperationsHeaderColumn(gutter + farmReadyTwoLine + gap, ranksTwoLine, 1));
    }

    private static void DrawOperationsHeaderLegend(OperationsHeaderLayout layout)
    {
        var origin = ImGui.GetCursorScreenPos() + new Vector2(FavoriteControlWidth, 0);
        DrawOperationsHeaderFields(
            origin,
            layout,
            new OperationsFcHeaderPresentation(
                "FC",
                "World",
                "Mode",
                "Next action / return",
                "Farm ready",
                "Ranks",
                false,
                false),
            legend: true);
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, layout.LegendHeight));
    }

    private static bool DrawAlignedOperationsHeader(
        string id,
        OperationsHeaderRenderContext context,
        OperationsHeaderLayout layout)
    {
        var origin = ImGui.GetCursorScreenPos();
        DrawFcProgressBackground(context.CurrentVoyages, layout.HeaderHeight);
        var style = ImGui.GetStyle();
        var paddingY = Math.Max(0f, (layout.HeaderHeight - ImGui.GetTextLineHeight()) / 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(style.FramePadding.X, paddingY));
        ImGui.PushStyleColor(ImGuiCol.Header, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(PlannerUi.PanelBackgroundAlt.X, PlannerUi.PanelBackgroundAlt.Y, PlannerUi.PanelBackgroundAlt.Z, 0.62f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(PlannerUi.PanelBackgroundAlt.X, PlannerUi.PanelBackgroundAlt.Y, PlannerUi.PanelBackgroundAlt.Z, 0.76f));
        var open = ImGui.CollapsingHeader($"###{id}");
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();
        DrawOperationsHeaderFields(origin, layout, context.Presentation, legend: false);
        return open;
    }

    private static void DrawOperationsHeaderFields(
        Vector2 origin,
        OperationsHeaderLayout layout,
        OperationsFcHeaderPresentation presentation,
        bool legend)
    {
        var normal = legend ? PlannerUi.Muted : ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        DrawOperationsHeaderCell(origin, layout, layout.FreeCompany, presentation.FreeCompany, normal);
        DrawOperationsHeaderCell(origin, layout, layout.World, presentation.World, normal);
        DrawOperationsHeaderCell(origin, layout, layout.Mode, presentation.Mode,
            legend ? PlannerUi.Muted : presentation.IsFarming ? PlannerUi.Green : PlannerUi.Teal);
        DrawOperationsHeaderCell(origin, layout, layout.Attention, presentation.Attention,
            legend ? PlannerUi.Muted : presentation.HasImmediateActions ? PlannerUi.Amber : PlannerUi.Cyan);
        DrawOperationsHeaderCell(origin, layout, layout.FarmReady, presentation.FarmReady,
            legend ? PlannerUi.Muted : presentation.IsFarming ? PlannerUi.Green : PlannerUi.Cyan);
        DrawOperationsHeaderCell(origin, layout, layout.Ranks, presentation.Ranks, normal);
    }

    private static void DrawOperationsHeaderCell(
        Vector2 origin,
        OperationsHeaderLayout layout,
        OperationsHeaderColumn column,
        string text,
        Vector4 color)
    {
        if (column.Width <= 1f)
            return;

        var scale = ImGuiHelpers.GlobalScale;
        var lineHeight = ImGui.GetTextLineHeight();
        var lineGap = 2f * scale;
        var contentHeight = layout.TwoLine ? (lineHeight * 2f) + lineGap : lineHeight;
        var firstLineY = origin.Y + ((layout.HeaderHeight - contentHeight) / 2f);
        var y = firstLineY + (column.Line * (lineHeight + lineGap));
        var padding = 3f * scale;
        var fitted = FitHeaderText(text, Math.Max(1f, column.Width - (padding * 2f)));
        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(
            new Vector2(origin.X + column.Offset, y),
            new Vector2(origin.X + column.Offset + column.Width, y + lineHeight),
            true);
        drawList.AddText(
            new Vector2(origin.X + column.Offset + padding, y),
            ImGui.ColorConvertFloat4ToU32(color),
            fitted);
        drawList.PopClipRect();
    }

    private void DrawOperationsHeaderTooltip(
        FcOperationalProjection projection,
        OperationsHeaderRenderContext context,
        DateTimeOffset now)
    {
        if (!ImGui.IsItemHovered())
            return;

        ImGui.BeginTooltip();
        ImGui.TextColored(PlannerUi.Teal, $"{projection.State.FreeCompanyTag} — {projection.State.World}");
        ImGui.TextUnformatted($"{context.Presentation.Mode} · Target R{projection.EffectiveTargetRank}");
        ImGui.TextUnformatted($"{context.Presentation.Attention} · Farm ready: {context.Presentation.FarmReady}");
        ImGui.Separator();
        foreach (var submarine in projection.Submarines)
        {
            ImGui.TextUnformatted(
                $"{submarine.Name}: R{submarine.Rank} · {submarine.CurrentBuild.Code} · {CompactOperationalStatePresentation.Create(submarine, now).Label}");
            if (submarine.CurrentBuild.UnavailableReason is not null)
                PlannerUi.Tooltip(submarine.CurrentBuild.UnavailableReason);
        }

        if (context.CurrentVoyages.Primary is { } primary)
        {
            ImGui.Separator();
            var state = projection.State.Submarines.FirstOrDefault(submarine => submarine.SubmarineId == primary.SubmarineId);
            if (state is not null)
                DrawCurrentVoyageTooltipContents(primary, state);
        }
        if (projection.CompletionP10AtUtc is { } p10 && projection.CompletionP90AtUtc is { } p90)
        {
            ImGui.Separator();
            ImGui.TextColored(PlannerUi.Muted, $"Likely ready between {FormatRelative(p10, now)} and {FormatRelative(p90, now)}");
        }
        ImGui.EndTooltip();
    }

    private static CompactFcHeaderLayout CalculateIncomeHeaderLayout(
        IEnumerable<IncomeFcHeaderPresentation> presentations, float availableWidth)
    {
        var values = presentations.ToArray();
        // Keep FC identity sized to its contents. Equal metric widths give the three
        // right-aligned values evenly spaced anchors instead of crowding the right edge.
        var metricWidth = Math.Max(
            MeasureHeaderColumn(values.Select(value => value.GrossGil), "Gross gil", 100f, 170f),
            Math.Max(
                MeasureHeaderColumn(values.Select(value => value.RecordedAverageGilPerDay), "Recorded avg/day", 135f, 185f),
                MeasureHeaderColumn(values.Select(value => value.Voyages), "Voyages", 65f, 95f)));
        return CalculateCompactFcHeaderLayout(
            [
                MeasureHeaderColumn(values.Select(value => value.FreeCompany), "FC tag", 70f, 135f),
                MeasureHeaderColumn(values.Select(value => value.World), "World", 90f, 150f),
                metricWidth,
                metricWidth,
                metricWidth,
            ],
            [0f, 0f, 1f, 1f, 1f], 2, availableWidth);
    }

    private static void DrawIncomeHeaderLegend(CompactFcHeaderLayout layout)
        => DrawCompactFcHeaderLegend(layout, ["FC tag", "World", "Gross gil", "Recorded avg/day", "Voyages"],
            rightAlignFrom: 2);

    private static void DrawIncomeHeaderFields(Vector2 origin, CompactFcHeaderLayout layout,
        IncomeFcHeaderPresentation presentation)
    {
        var normal = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        DrawCompactFcHeaderCell(origin, layout, 0, presentation.FreeCompany, normal);
        DrawCompactFcHeaderCell(origin, layout, 1, presentation.World, normal);
        DrawCompactFcHeaderCell(origin, layout, 2, presentation.GrossGil, normal, rightAligned: true);
        DrawCompactFcHeaderCell(origin, layout, 3, presentation.RecordedAverageGilPerDay, normal, rightAligned: true);
        DrawCompactFcHeaderCell(origin, layout, 4, presentation.Voyages, normal, rightAligned: true);
    }

    private static void DrawIncomeHeaderTooltip(FcOperationalProjection projection, IncomeFcMetrics metric)
    {
        if (!ImGui.IsItemHovered())
            return;

        ImGui.BeginTooltip();
        ImGui.TextColored(PlannerUi.Teal, $"{projection.State.FreeCompanyTag} — {projection.State.World}");
        ImGui.TextUnformatted($"{projection.Mode} · {metric.VoyageCount:N0} tracked voyage{(metric.VoyageCount == 1 ? string.Empty : "s")}");
        ImGui.Separator();
        ImGui.TextColored(PlannerUi.Green, $"Gross NPC salvage value: {metric.GrossGil:N0} gil");
        ImGui.TextUnformatted($"Recorded average per day: {metric.RecordedAverageGilPerDay:N0} gil");
        ImGui.TextUnformatted($"Gil per voyage: {metric.GilPerVoyage:N0}");
        ImGui.TextColored(PlannerUi.Muted, $"Coverage: {FormatIncomeDate(metric.FirstReturnAtUtc)} – {FormatIncomeDate(metric.LastReturnAtUtc)}");
        ImGui.Separator();
        ImGui.TextColored(PlannerUi.Teal, "Current builds and ranks");
        foreach (var submarine in metric.Submarines)
        {
            ImGui.TextUnformatted($"{submarine.Name}: {submarine.CurrentBuild.Code} · R{submarine.Rank}");
            if (submarine.CurrentBuild.UnavailableReason is not null)
                PlannerUi.Tooltip(submarine.CurrentBuild.UnavailableReason);
        }
        ImGui.TextColored(PlannerUi.Muted, "Current tracker values; recorded income may come from earlier ranks or builds.");
        ImGui.EndTooltip();
    }
}
