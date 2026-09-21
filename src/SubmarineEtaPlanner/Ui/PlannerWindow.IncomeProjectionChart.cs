using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using SubmarineEtaPlanner.Planner;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

public sealed partial class PlannerWindow
{
    private readonly IncomeProjectionChartCache incomeProjectionChartCache = new();

    private void DrawIncomeProjectionChart(IncomeProjectionTotals totals, IncomeProjectionHorizon horizon, DateTimeOffset now)
    {
        ImGui.SetNextItemOpen(this.configuration.ShowIncomeProjectionChart, ImGuiCond.Always);
        var open = ImGui.CollapsingHeader("Estimated income chart###projection-chart");
        PlannerUi.Tooltip("Show or hide the projection chart. Saved automatically.");
        if (open != this.configuration.ShowIncomeProjectionChart)
        {
            this.configuration.ShowIncomeProjectionChart = open;
            this.saveConfiguration();
        }
        if (totals.IncludesApproximations)
            PlannerUi.WrappedText($"Chart includes previous-rank approximations · {totals.MatchCoverage}.", PlannerUi.Amber);
        var series = this.incomeProjectionChartCache.Get(open, totals, horizon, now, TimeZoneInfo.Local);
        if (series is null) return;
        if (series.EstimatedGil is null)
        {
            PlannerUi.WrappedText("The chart is unavailable until at least one farming submarine has an estimate.", PlannerUi.Muted);
            return;
        }
        DrawIncomeProjectionChartCanvas(series);
        PlannerUi.WrappedText($"{series.Title} · {(totals.IsPartial ? "partial total · " : string.Empty)}* Part of a calendar period. Bars show estimated value at a steady pace, not scheduled collections.", PlannerUi.Muted);
    }

    private static void DrawIncomeProjectionChartCanvas(IncomeProjectionChartSeries series)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(Math.Max(1f, ImGui.GetContentRegionAvail().X), 118f * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var draw = ImGui.GetWindowDrawList();
        ImGui.InvisibleButton("projection-chart-canvas", size);
        var hovered = ImGui.IsItemHovered();
        var topLabel = FormatIncomeChartGil(series.AxisMaximum);
        var middleLabel = FormatIncomeChartGil(series.AxisMaximum / 2);
        var axisWidth = Math.Max(ImGui.CalcTextSize(topLabel).X, ImGui.CalcTextSize(middleLabel).X) + 12f * scale;
        var plotStart = origin + new Vector2(axisWidth, 12f * scale);
        var plotEnd = end - new Vector2(8f * scale, 26f * scale);
        var width = plotEnd.X - plotStart.X;
        if (width <= 1f || series.Buckets.Count == 0) return;
        var height = plotEnd.Y - plotStart.Y;
        var slot = width / series.Buckets.Count;
        var mouse = ImGui.GetMousePos();
        var hoverIndex = hovered && mouse.X >= plotStart.X && mouse.X < plotEnd.X && mouse.Y >= plotStart.Y && mouse.Y <= plotEnd.Y
            ? Math.Clamp((int)((mouse.X - plotStart.X) / slot), 0, series.Buckets.Count - 1) : -1;
        var teal = ImGui.ColorConvertFloat4ToU32(PlannerTheme.WithAlpha(PlannerUi.Teal, 0.75f));
        var muted = ImGui.ColorConvertFloat4ToU32(PlannerUi.Muted);
        var border = ImGui.ColorConvertFloat4ToU32(PlannerUi.Border);
        draw.PushClipRect(origin, end, true);
        draw.AddRectFilled(origin, end, ImGui.ColorConvertFloat4ToU32(PlannerUi.PanelBackground), 5f * scale);
        for (var tick = 0; tick <= 2; tick++)
        {
            var y = plotEnd.Y - height * tick / 2;
            draw.AddLine(new(plotStart.X, y), new(plotEnd.X, y), border);
            var label = tick == 0 ? "0" : tick == 1 ? middleLabel : topLabel;
            draw.AddText(new(plotStart.X - ImGui.CalcTextSize(label).X - 6f * scale, y - ImGui.GetTextLineHeight() / 2), muted, label);
        }
        for (var index = 0; index < series.Buckets.Count; index++)
        {
            var bucket = series.Buckets[index];
            var left = plotStart.X + slot * index;
            var right = left + slot;
            var center = (left + right) / 2;
            var inset = Math.Min(2f * scale, slot * 0.2f);
            if (hoverIndex == index)
                draw.AddRectFilled(new(left, plotStart.Y), new(right, plotEnd.Y), ImGui.ColorConvertFloat4ToU32(PlannerTheme.WithAlpha(PlannerUi.Cyan, 0.10f)));
            if (bucket.EstimatedGil > 0)
            {
                var barHeight = Math.Max(1f, (float)((double)bucket.EstimatedGil / series.AxisMaximum) * height);
                draw.AddRectFilled(new(left + inset, plotEnd.Y - barHeight), new(right - inset, plotEnd.Y), teal);
            }
            else draw.AddLine(new(left + inset, plotEnd.Y), new(right - inset, plotEnd.Y), teal, Math.Max(1f, scale));
            if (bucket.IsPartial) draw.AddText(new(center - ImGui.CalcTextSize("*").X / 2, origin.Y), muted, "*");
        }
        var ticks = Math.Clamp((int)(width / (75f * scale)), 2, 5);
        var stride = Math.Max(1, (int)Math.Ceiling((series.Buckets.Count - 1d) / (ticks - 1)));
        var previousRight = plotStart.X - 8f * scale;
        for (var index = 0; index < series.Buckets.Count; index++)
        {
            if (index % stride != 0 && index != series.Buckets.Count - 1) continue;
            var label = series.Buckets[index].StartDate.ToString(series.Monthly ? "MMM yy" : "d MMM");
            var labelWidth = ImGui.CalcTextSize(label).X;
            var x = Math.Clamp(plotStart.X + slot * (index + 0.5f) - labelWidth / 2,
                plotStart.X, Math.Max(plotStart.X, plotEnd.X - labelWidth));
            if (x < previousRight + 8f * scale) continue;
            draw.AddText(new(x, plotEnd.Y + 8f * scale), muted, label);
            previousRight = x + labelWidth;
        }
        draw.PopClipRect();
        if (hoverIndex < 0) return;
        var selected = series.Buckets[hoverIndex];
        PlannerUi.BeginTooltip();
        PlannerUi.WrappedText(selected.StartDate == selected.EndDate ? selected.StartDate.ToString("D")
            : $"{selected.StartDate:d} – {selected.EndDate:d}", PlannerUi.Cyan);
        PlannerUi.WrappedText($"Estimated gross NPC value: {selected.EstimatedGil:N0} gil");
        PlannerUi.WrappedText(series.Totals.Coverage);
        PlannerUi.WrappedText(series.Totals.MatchCoverage);
        if (series.Totals.IncludesApproximations) PlannerUi.WrappedText("Includes previous-rank approximations.", PlannerUi.Amber);
        if (series.Totals.IsPartial) PlannerUi.WrappedText("Partial: includes only submarines with an estimate.", PlannerUi.Amber);
        if (selected.IsPartial) PlannerUi.WrappedText("The selected horizon includes only part of this calendar period.", PlannerUi.Muted);
        PlannerUi.WrappedText($"From {selected.StartAtUtc.LocalDateTime:g} to {selected.EndAtUtc.LocalDateTime:g}. Current pace is assumed throughout; this is not a collection schedule.");
        PlannerUi.EndTooltip();
    }
}
