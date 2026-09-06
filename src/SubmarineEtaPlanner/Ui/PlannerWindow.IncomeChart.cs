using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using SubmarineEtaPlanner.Planner;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

public sealed partial class PlannerWindow
{
    private readonly IncomeChartCache incomeChartCache = new();

    private void DrawIncomeChart(IReadOnlyList<FcState> source, IReadOnlySet<string> fcIds,
        TimeSpan? period, DateTimeOffset now)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.SetNextItemOpen(this.configuration.ShowIncomeChart, ImGuiCond.Always);
        var open = ImGui.CollapsingHeader("###income-chart");
        PlannerUi.Tooltip("Show or hide recorded income history. Saved automatically.");
        if (open != this.configuration.ShowIncomeChart)
        {
            this.configuration.ShowIncomeChart = open;
            this.saveConfiguration();
        }

        var series = this.incomeChartCache.Get(open, source, fcIds, period, now, TimeZoneInfo.Local);
        var title = FitHeaderText(series?.Title ?? "Recorded income history", Math.Max(1f, width - 56f * scale));
        var titleSize = ImGui.CalcTextSize(title);
        ImGui.GetWindowDrawList().AddText(origin + new Vector2(Math.Max(28f * scale, (width - titleSize.X) / 2),
            (ImGui.GetFrameHeight() - titleSize.Y) / 2), ImGui.GetColorU32(ImGuiCol.Text), title);
        if (series is null) return;
        if (series.FcCount == 0)
        {
            PlannerUi.WrappedText("No FCs in this view.", PlannerUi.Muted);
            return;
        }
        if (series.HistoryNotices.Count > 0)
        {
            PlannerUi.WrappedText(series.HistoryUnavailable ? "Recorded income history is unavailable."
                : $"History availability is unknown or unavailable for {series.HistoryNotices.Count} of {series.FcCount} FCs.", PlannerUi.Amber);
            if (series.HistoryUnavailable)
                PlannerUi.WrappedText(series.HistoryNotices[0], PlannerUi.Muted);
            if (ImGui.IsItemHovered())
            {
                BeginIncomeChartTooltip();
                foreach (var notice in series.HistoryNotices.Take(6)) PlannerUi.WrappedText(notice);
                if (series.HistoryNotices.Count > 6)
                    PlannerUi.WrappedText($"And {series.HistoryNotices.Count - 6} more FCs. Use an FC's Income shortcut to inspect it.", PlannerUi.Muted);
                ImGui.EndTooltip();
            }
        }
        if (!series.HasRecordedReturns)
        {
            if (!series.HistoryUnavailable)
                PlannerUi.WrappedText("No recorded returns in this period.", PlannerUi.Muted);
            return;
        }

        DrawIncomeChartCanvas(series);
        PlannerUi.WrappedText("Bars: recorded gil · Dots: 0 gil recorded · Hatching: days without entries · * Incomplete", PlannerUi.Muted);
        if (ImGui.IsItemHovered())
        {
            BeginIncomeChartTooltip();
            PlannerUi.WrappedText("Days without entries have no recorded returns; history may be incomplete. They are not assumed to be zero income. Today and partly included boundary periods are marked incomplete.");
            ImGui.EndTooltip();
        }
    }

    private static void DrawIncomeChartCanvas(IncomeChartSeries series)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(Math.Max(1f, ImGui.GetContentRegionAvail().X), 118f * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var draw = ImGui.GetWindowDrawList();
        ImGui.InvisibleButton("income-chart-canvas", size);
        var hovered = ImGui.IsItemHovered();
        var axisMaximum = series.AxisMaximum;
        var topLabel = FormatIncomeChartGil(axisMaximum);
        var middleLabel = FormatIncomeChartGil(axisMaximum / 2);
        var axisWidth = Math.Max(ImGui.CalcTextSize(topLabel).X, ImGui.CalcTextSize(middleLabel).X) + 12f * scale;
        var plotStart = origin + new Vector2(axisWidth, 12f * scale);
        var plotEnd = end - new Vector2(8f * scale, 26f * scale);
        var plotWidth = plotEnd.X - plotStart.X;
        if (plotWidth <= 1f) return;
        var plotHeight = plotEnd.Y - plotStart.Y;
        var slotWidth = plotWidth / series.Buckets.Count;
        var mouse = ImGui.GetMousePos();
        var hoverIndex = hovered && mouse.X >= plotStart.X && mouse.X < plotEnd.X
            && mouse.Y >= plotStart.Y && mouse.Y <= plotEnd.Y + 6f * scale
            ? Math.Clamp((int)((mouse.X - plotStart.X) / slotWidth), 0, series.Buckets.Count - 1) : -1;
        var green = ImGui.ColorConvertFloat4ToU32(new Vector4(0.27f, 0.61f, 0.46f, 0.9f));
        var muted = ImGui.ColorConvertFloat4ToU32(PlannerUi.Muted);
        var border = ImGui.ColorConvertFloat4ToU32(PlannerUi.Border);
        draw.PushClipRect(origin, end, true);
        draw.AddRectFilled(origin, end, ImGui.ColorConvertFloat4ToU32(PlannerUi.PanelBackground), 5f * scale);
        for (var tick = 0; tick <= 2; tick++)
        {
            var y = plotEnd.Y - plotHeight * tick / 2;
            draw.AddLine(new(plotStart.X, y), new(plotEnd.X, y), border);
            var label = tick == 0 ? "0" : tick == 1 ? middleLabel : topLabel;
            draw.AddText(new(plotStart.X - ImGui.CalcTextSize(label).X - 6f * scale,
                y - ImGui.GetTextLineHeight() / 2), muted, label);
        }
        for (var index = 0; index < series.Buckets.Count; index++)
        {
            var bucket = series.Buckets[index];
            var left = plotStart.X + slotWidth * index;
            var right = left + slotWidth;
            var center = (left + right) / 2;
            var inset = Math.Min(2f * scale, slotWidth * 0.2f);
            if (index == hoverIndex)
                draw.AddRectFilled(new(left, plotStart.Y), new(right, plotEnd.Y),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(PlannerUi.Cyan.X, PlannerUi.Cyan.Y, PlannerUi.Cyan.Z, 0.12f)));
            if (bucket.State == IncomeChartBucketState.RecordedGil)
            {
                var height = Math.Max(1f, (float)(bucket.GrossGil / axisMaximum) * plotHeight);
                draw.AddRectFilled(new(left + inset, plotEnd.Y - height), new(right - inset, plotEnd.Y), green);
            }
            else if (bucket.State == IncomeChartBucketState.RecordedZero)
            {
                draw.AddCircle(new(center, plotEnd.Y - 2f * scale), Math.Min(2f * scale, slotWidth * 0.3f), muted, 8, Math.Max(1f, scale));
            }
            else
            {
                draw.AddRectFilled(new(left + inset, plotStart.Y), new(right - inset, plotEnd.Y),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.62f, 0.72f, 0.76f, 0.05f)));
            }
            if (bucket.DaysWithoutReturns > 0)
            {
                var stripeTop = plotEnd.Y + 1f * scale;
                var stripeBottom = plotEnd.Y + 6f * scale;
                draw.PushClipRect(new(left, stripeTop), new(right, stripeBottom), true);
                for (var x = left - 5f * scale; x < right; x += 5f * scale)
                    draw.AddLine(new(x, stripeBottom), new(x + 5f * scale, stripeTop), border);
                draw.PopClipRect();
            }
            if (bucket.IsPartial)
                draw.AddText(new(center - ImGui.CalcTextSize("*").X / 2, origin.Y), muted, "*");
        }

        var ticks = Math.Clamp((int)(plotWidth / (75f * scale)), 2, 5);
        var stride = Math.Max(1, (int)Math.Ceiling((series.Buckets.Count - 1d) / (ticks - 1)));
        var previousRight = plotStart.X - 8f * scale;
        for (var index = 0; index < series.Buckets.Count; index++)
        {
            if (index % stride != 0 && index != series.Buckets.Count - 1) continue;
            var label = series.Buckets[index].StartDate.ToString(series.Grouping == IncomeChartGrouping.Monthly ? "MMM yy" : "d MMM");
            var labelWidth = ImGui.CalcTextSize(label).X;
            var x = Math.Clamp(plotStart.X + slotWidth * (index + 0.5f) - labelWidth / 2,
                plotStart.X, Math.Max(plotStart.X, plotEnd.X - labelWidth));
            if (x < previousRight + 8f * scale) continue;
            draw.AddText(new(x, plotEnd.Y + 8f * scale), muted, label);
            previousRight = x + labelWidth;
        }
        draw.PopClipRect();
        if (hoverIndex >= 0) DrawIncomeChartTooltip(series, series.Buckets[hoverIndex]);
    }

    private static string FormatIncomeChartGil(double value)
        => value >= 1_000_000_000 ? $"{value / 1_000_000_000:0.#}b"
            : value >= 1_000_000 ? $"{value / 1_000_000:0.#}m"
            : value >= 1_000 ? $"{value / 1_000:0.#}k" : $"{value:0.#}";

    private static void BeginIncomeChartTooltip()
    {
        var width = Math.Min(380f * ImGuiHelpers.GlobalScale, Math.Max(1f, ImGui.GetIO().DisplaySize.X - 24f * ImGuiHelpers.GlobalScale));
        ImGui.SetNextWindowSizeConstraints(new(width, 0f), new(width, float.MaxValue));
        ImGui.BeginTooltip();
    }

    private static void DrawIncomeChartTooltip(IncomeChartSeries series, IncomeChartBucket bucket)
    {
        BeginIncomeChartTooltip();
        PlannerUi.WrappedText(bucket.StartDate == bucket.EndDate ? bucket.StartDate.ToString("D")
            : $"{bucket.StartDate:d} – {bucket.EndDate:d}", PlannerUi.Cyan);
        if (bucket.State == IncomeChartBucketState.NoRecordedReturns)
            PlannerUi.WrappedText("No recorded returns. Income is unknown for this interval.");
        else
            PlannerUi.WrappedText($"{bucket.GrossGil:N0} gil recorded · gross NPC salvage value");
        PlannerUi.WrappedText($"Recorded returns: {bucket.RecordedReturns:N0} · FCs with returns: {bucket.FcCount} of {series.FcCount}");
        if (series.Grouping != IncomeChartGrouping.Daily)
            PlannerUi.WrappedText($"Days with entries: {bucket.DaysWithReturns} · Days without entries: {bucket.DaysWithoutReturns}");
        if (bucket.IsPartial)
            PlannerUi.WrappedText(bucket.IncludesToday ? "Incomplete: includes today or an unfinished calendar period."
                : "Incomplete: the selected time window includes only part of this calendar period.", PlannerUi.Amber);
        PlannerUi.WrappedText("Days without entries are not assumed to be zero income. Recorded-return counts include returns with no salvage gil; the summary voyage count includes salvage returns only.", PlannerUi.Muted);
        ImGui.EndTooltip();
    }
}
