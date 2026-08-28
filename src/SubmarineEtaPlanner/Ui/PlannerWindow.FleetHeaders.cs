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

    private readonly record struct IncomeHeaderColumn(float Offset, float Width, int Line);

    private sealed record IncomeHeaderLayout(
        bool TwoLine,
        float HeaderHeight,
        float LegendHeight,
        IncomeHeaderColumn FreeCompany,
        IncomeHeaderColumn World,
        IncomeHeaderColumn Mode,
        IncomeHeaderColumn GrossGil,
        IncomeHeaderColumn RecordedAverageGilPerDay,
        IncomeHeaderColumn GilPerVoyage,
        IncomeHeaderColumn Voyages,
        IncomeHeaderColumn Submarine1,
        IncomeHeaderColumn Submarine2,
        IncomeHeaderColumn Submarine3,
        IncomeHeaderColumn Submarine4);

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
        var origin = ImGui.GetCursorScreenPos();
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

    private static IncomeHeaderLayout CalculateIncomeHeaderLayout(
        IEnumerable<IncomeFcHeaderPresentation> presentations,
        float availableWidth)
    {
        var values = presentations.ToArray();
        var scale = ImGuiHelpers.GlobalScale;
        var gap = 8f * scale;
        var gutter = 40f * scale;
        var fcWidth = MeasureHeaderColumn(values.Select(value => value.FreeCompany), "FC", 90f, 155f);
        var worldWidth = MeasureHeaderColumn(values.Select(value => value.World), "World", 85f, 145f);
        var modeWidth = MeasureHeaderColumn(values.Select(value => value.Mode), "Mode", 78f, 105f);
        var grossWidth = MeasureHeaderColumn(values.Select(value => value.GrossGil), "Gross gil", 95f, 145f);
        var recordedAverageWidth = MeasureHeaderColumn(values.Select(value => value.RecordedAverageGilPerDay), "Recorded avg / day", 105f, 145f);
        var voyageWidth = MeasureHeaderColumn(values.Select(value => value.GilPerVoyage), "Gil / voyage", 100f, 140f);
        var countWidth = MeasureHeaderColumn(values.Select(value => value.Voyages), "Voyages", 72f, 100f);
        var submarine1Width = MeasureHeaderColumn(values.Select(value => value.Submarine1), "Sub #1", 95f, 135f);
        var submarine2Width = MeasureHeaderColumn(values.Select(value => value.Submarine2), "Sub #2", 95f, 135f);
        var submarine3Width = MeasureHeaderColumn(values.Select(value => value.Submarine3), "Sub #3", 95f, 135f);
        var submarine4Width = MeasureHeaderColumn(values.Select(value => value.Submarine4), "Sub #4", 95f, 135f);
        var widths = FitIncomeHeaderWidths(
            [fcWidth, worldWidth, modeWidth, grossWidth, recordedAverageWidth, voyageWidth, countWidth, submarine1Width, submarine2Width, submarine3Width, submarine4Width],
            [72f * scale, 66f * scale, 60f * scale, 80f * scale, 86f * scale, 78f * scale, 48f * scale, 76f * scale, 76f * scale, 76f * scale, 76f * scale],
            Math.Max(1f, availableWidth - gutter - (gap * 10f)));
        var offset = gutter;
        IncomeHeaderColumn NextColumn(int index)
        {
            var column = new IncomeHeaderColumn(offset, widths[index], 0);
            offset += widths[index] + gap;
            return column;
        }
        return new IncomeHeaderLayout(
            false,
            ImGui.GetFrameHeight(),
            ImGui.GetFrameHeight(),
            NextColumn(0),
            NextColumn(1),
            NextColumn(2),
            NextColumn(3),
            NextColumn(4),
            NextColumn(5),
            NextColumn(6),
            NextColumn(7),
            NextColumn(8),
            NextColumn(9),
            NextColumn(10));
    }

    private static float[] FitIncomeHeaderWidths(
        IReadOnlyList<float> desiredWidths,
        IReadOnlyList<float> minimumWidths,
        float availableWidth)
    {
        var desiredTotal = desiredWidths.Sum();
        if (availableWidth >= desiredTotal)
        {
            var widths = desiredWidths.ToArray();
            var submarineColumnCount = Math.Min(4, widths.Length);
            var surplusPerSubmarine = (availableWidth - desiredTotal) / Math.Max(1, submarineColumnCount);
            for (var index = widths.Length - submarineColumnCount; index < widths.Length; index++)
                widths[index] += surplusPerSubmarine;
            return widths;
        }

        var minimumTotal = minimumWidths.Sum();
        if (availableWidth <= minimumTotal)
        {
            var compactScale = availableWidth / minimumTotal;
            return minimumWidths.Select(width => Math.Max(1f, width * compactScale)).ToArray();
        }

        var expansion = (availableWidth - minimumTotal) / Math.Max(1f, desiredTotal - minimumTotal);
        return desiredWidths
            .Select((width, index) => minimumWidths[index] + ((width - minimumWidths[index]) * expansion))
            .ToArray();
    }

    private static void DrawIncomeHeaderLegend(IncomeHeaderLayout layout)
    {
        var origin = ImGui.GetCursorScreenPos();
        DrawIncomeHeaderFields(
            origin,
            layout,
            new IncomeFcHeaderPresentation(
                "income-legend",
                "FC",
                "World",
                "Mode",
                "Gross gil",
                "Recorded avg / day",
                "Gil / voyage",
                "Voyages",
                "Sub #1",
                "Sub #2",
                "Sub #3",
                "Sub #4",
                false),
            legend: true);
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, layout.LegendHeight));
    }

    private static void DrawIncomeHeaderFields(
        Vector2 origin,
        IncomeHeaderLayout layout,
        IncomeFcHeaderPresentation presentation,
        bool legend)
    {
        var normal = legend ? PlannerUi.Muted : ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        DrawIncomeHeaderCell(origin, layout, layout.FreeCompany, presentation.FreeCompany, normal);
        DrawIncomeHeaderCell(origin, layout, layout.World, presentation.World, normal);
        DrawIncomeHeaderCell(origin, layout, layout.Mode, presentation.Mode,
            legend ? PlannerUi.Muted : presentation.IsFarming ? PlannerUi.Green : PlannerUi.Teal);
        DrawIncomeHeaderCell(origin, layout, layout.GrossGil, presentation.GrossGil,
            legend ? PlannerUi.Muted : PlannerUi.Green);
        DrawIncomeHeaderCell(origin, layout, layout.RecordedAverageGilPerDay, presentation.RecordedAverageGilPerDay, normal);
        DrawIncomeHeaderCell(origin, layout, layout.GilPerVoyage, presentation.GilPerVoyage, normal);
        DrawIncomeHeaderCell(origin, layout, layout.Voyages, presentation.Voyages, normal);
        DrawIncomeHeaderCell(origin, layout, layout.Submarine1, presentation.Submarine1, normal);
        DrawIncomeHeaderCell(origin, layout, layout.Submarine2, presentation.Submarine2, normal);
        DrawIncomeHeaderCell(origin, layout, layout.Submarine3, presentation.Submarine3, normal);
        DrawIncomeHeaderCell(origin, layout, layout.Submarine4, presentation.Submarine4, normal);
    }

    private static void DrawIncomeHeaderCell(
        Vector2 origin,
        IncomeHeaderLayout layout,
        IncomeHeaderColumn column,
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
