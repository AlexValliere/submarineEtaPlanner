using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using SubmarineEtaPlanner.Planner;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

public sealed partial class PlannerWindow
{
    private readonly IncomeProjectionCache incomeProjectionCache = new();

    private void DrawIncomeProjection(EtaPlannerSnapshot currentSnapshot)
    {
        var first = true;
        foreach (var horizon in new[] { IncomeProjectionHorizon.Days30, IncomeProjectionHorizon.Days90, IncomeProjectionHorizon.Days365 })
        {
            var label = $"{(int)horizon} days";
            if (!first) PlannerUi.SameLineIfFits(label);
            first = false;
            if (PlannerUi.SegmentedButton($"projection-horizon-{horizon}", label, this.configuration.IncomeProjectionHorizon == horizon))
            {
                this.configuration.IncomeProjectionHorizon = horizon;
                this.saveConfiguration();
            }
        }
        var allowPreviousRank = this.configuration.AllowPreviousRankIncomeApproximations;
        PlannerUi.SameLineIfFits("Allow previous-rank approximations", ImGui.GetFrameHeight());
        if (ImGui.Checkbox("Allow previous-rank approximations", ref allowPreviousRank))
        {
            this.configuration.AllowPreviousRankIncomeApproximations = allowPreviousRank;
            this.saveConfiguration();
        }
        PlannerUi.Tooltip("Saved automatically. When exact history is insufficient, allow at least 10 compatible returns from the immediately previous rank. Exact matches always take priority.");
        var selectedHorizon = this.configuration.IncomeProjectionHorizon;
        PlannerUi.WrappedText($"Estimated over {(int)selectedHorizon} days at current pace · based on the last 90 days of history.", PlannerUi.Muted);
        var now = DateTimeOffset.UtcNow;
        var result = this.incomeProjectionCache.Get(currentSnapshot.FreeCompanies,
            this.configuration.FreeCompanyPreferences, this.configuration.Settings, this.catalog, this.operationalCatalog, now,
            allowPreviousRank);
        var fleets = IncomeProjectionPresentation.Select(result, this.incomeFcScope,
            fcId => this.configuration.FreeCompanyPreferences.GetValueOrDefault(fcId)?.Favorite == true);
        var totals = IncomeProjectionPresentation.Summarize(fleets);
        if (totals.FarmingSubmarines == 0)
        {
            PlannerUi.Callout("projection-no-farmers", FontAwesomeIcon.InfoCircle, "No farming submarines",
                this.incomeFcScope is null ? "Assign a submarine to Farming in FC Setup to estimate its income."
                    : "This FC has no submarines with the Farming role. Leveling and paused submarines are excluded.", PlannerUi.Muted);
            return;
        }

        ImGui.Spacing();
        if (ImGui.BeginTable("projection-summary", ImGui.GetContentRegionAvail().X < 680f * ImGuiHelpers.GlobalScale ? 2 : 3,
                ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextColumn();
            PlannerUi.MetricCard(this.typography, "projection-total", FontAwesomeIcon.Coins,
                FormatProjectedGil(totals.ProjectedGil(selectedHorizon), compact: true),
                totals.IsPartial ? "Estimated gil · partial" : "Estimated gil",
                totals.IsPartial || totals.IncludesApproximations ? PlannerUi.Amber : PlannerUi.Green);
            ImGui.TableNextColumn();
            PlannerUi.MetricCard(this.typography, "projection-rate", FontAwesomeIcon.CalendarDay,
                FormatProjectedGil(totals.GilPerDay, compact: true), "Estimated gil / day", PlannerUi.Teal);
            ImGui.TableNextColumn();
            PlannerUi.MetricCard(this.typography, "projection-coverage", FontAwesomeIcon.Ship,
                $"{totals.EstimatedSubmarines} / {totals.FarmingSubmarines}", "Farming submarines estimated", PlannerUi.Cyan);
            ImGui.EndTable();
        }
        PlannerUi.WrappedText($"{fleets.Count} FCs shown · {totals.Coverage}" +
            (totals.IsPartial ? " · Partial: total includes only submarines with an estimate." : string.Empty),
            totals.IsPartial ? PlannerUi.Amber : PlannerUi.Muted);
        PlannerUi.WrappedText(totals.MatchCoverage +
            (totals.IncludesApproximations ? " · Total includes previous-rank approximations." : string.Empty),
            totals.IncludesApproximations ? PlannerUi.Amber : PlannerUi.Muted);
        if (totals.EstimatedSubmarines == 0)
            PlannerUi.WrappedText("No estimate is available yet. Expand an FC to see missing setup or matching history.", PlannerUi.Amber);
        if (result.HistoryNotices.Count > 0)
        {
            PlannerUi.WrappedText($"History availability is unknown or unavailable for {result.HistoryNotices.Count} visible FCs.", PlannerUi.Amber);
            if (ImGui.IsItemHovered())
            {
                PlannerUi.BeginTooltip();
                foreach (var notice in result.HistoryNotices.Take(6)) PlannerUi.WrappedText(notice);
                PlannerUi.EndTooltip();
            }
        }
        PlannerUi.WrappedText("Assumes current builds, routes and collection delays continue, fuel is replenished, and there is no additional downtime. Gross NPC salvage value; costs are not deducted.", PlannerUi.Muted);
        PlannerUi.WrappedText("Requires 10 matching returns. Sample counts describe supporting history, not statistical confidence. Returns worth 0 gil are included.", PlannerUi.Muted);
        if (allowPreviousRank)
            PlannerUi.WrappedText("Approximate results reuse compatible history from one rank earlier at the current cycle duration. The historical average is not adjusted for changed stats or future leveling.", PlannerUi.Muted);
        ImGui.Spacing();
        DrawIncomeProjectionChart(totals, selectedHorizon, now);
        ImGui.Spacing();

        var layout = CalculateCompactFcHeaderLayout(
            [MeasureHeaderColumn(fleets.Select(fc => fc.FreeCompanyTag), "FC tag", 70f, 135f),
             MeasureHeaderColumn(fleets.Select(fc => fc.World), "World", 90f, 150f),
             MeasureHeaderColumn(fleets.Select(fc => FormatProjectedGil(fc.Totals.ProjectedGil(selectedHorizon))), "Estimated gil", 115f, 190f),
             MeasureHeaderColumn(fleets.Select(fc => FormatProjectedGil(fc.Totals.GilPerDay)), "Est. gil/day", 100f, 160f),
             MeasureHeaderColumn(fleets.Select(fc => $"{fc.Totals.EstimatedSubmarines} / {fc.Totals.FarmingSubmarines}"), "Estimated subs", 105f, 130f)],
            [0f, 0f, 1f, 1f, 1f], 2, ImGui.GetContentRegionAvail().X - FavoriteControlWidth);
        DrawCompactFcHeaderLegend(layout, ["FC tag", "World", "Estimated gil", "Est. gil/day", "Estimated subs"], centerFrom: 2);
        foreach (var fc in fleets) DrawIncomeProjectionFleet(fc, selectedHorizon, layout);
    }

    private void DrawIncomeProjectionFleet(IncomeFcProjection fc, IncomeProjectionHorizon horizon, CompactFcHeaderLayout layout)
    {
        ImGui.Spacing();
        DrawFavoriteControl(fc.FcIdKey);
        if (this.expandIncomeFc == fc.FcIdKey)
        {
            ImGui.SetNextItemOpen(true, ImGuiCond.Always);
            this.expandIncomeFc = null;
        }
        var origin = ImGui.GetCursorScreenPos();
        var style = ImGui.GetStyle();
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(style.FramePadding.X,
            Math.Max(0f, (layout.HeaderHeight - ImGui.GetTextLineHeight()) / 2f)));
        ImGui.PushStyleColor(ImGuiCol.Header, PlannerUi.PanelBackgroundAlt);
        var open = ImGui.CollapsingHeader($"###projection-fc-{fc.FcIdKey}");
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        var color = fc.Totals.IsPartial || fc.Totals.IncludesApproximations ? PlannerUi.Amber : ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        DrawCompactFcHeaderCell(origin, layout, 0, fc.FreeCompanyTag, color);
        DrawCompactFcHeaderCell(origin, layout, 1, string.IsNullOrWhiteSpace(fc.World) ? "—" : fc.World, color);
        DrawCompactFcHeaderCell(origin, layout, 2, FormatProjectedGil(fc.Totals.ProjectedGil(horizon)), color, centered: true);
        DrawCompactFcHeaderCell(origin, layout, 3, FormatProjectedGil(fc.Totals.GilPerDay), color, centered: true);
        DrawCompactFcHeaderCell(origin, layout, 4, $"{fc.Totals.EstimatedSubmarines} / {fc.Totals.FarmingSubmarines}", color, centered: true);
        if (ImGui.IsItemHovered())
        {
            PlannerUi.BeginTooltip();
            PlannerUi.WrappedText(fc.DisplayName, PlannerUi.Teal);
            PlannerUi.WrappedText($"Estimated over {(int)horizon} days: {FormatProjectedGil(fc.Totals.ProjectedGil(horizon))} gil");
            PlannerUi.WrappedText(fc.Totals.Coverage);
            PlannerUi.WrappedText(fc.Totals.MatchCoverage);
            if (fc.Totals.IncludesApproximations) PlannerUi.WrappedText("This total includes previous-rank approximations.", PlannerUi.Amber);
            if (fc.Totals.IsPartial) PlannerUi.WrappedText("Partial: this total includes only submarines with an estimate.", PlannerUi.Amber);
            PlannerUi.EndTooltip();
        }
        if (!open) return;
        ImGui.Spacing();
        DrawFcShortcuts(fc.FcIdKey);
        if (fc.Totals.IsPartial) PlannerUi.WrappedText("Partial estimate · missing submarines are excluded from the total.", PlannerUi.Amber);
        DrawIncomeProjectionSubmarines(fc, horizon);
        foreach (var sub in fc.Submarines.Where(sub => !sub.IsAvailable))
            PlannerUi.WrappedText($"{sub.Name}: {sub.UnavailableReason}" +
                (sub.Sample.ReturnCount < IncomeProjectionCalculator.MinimumReturns
                    ? $" · {FormatProjectionMatchCounts(sub)}." : string.Empty), PlannerUi.Amber);
    }

    private void DrawIncomeProjectionSubmarines(IncomeFcProjection fc, IncomeProjectionHorizon horizon)
    {
        string Route(IncomeSubmarineProjection sub) => RouteDisplayFormatter.FormatCompactRoute(sub.Route, this.catalog.PointName);
        var layout = CalculateResponsiveTableLayout(ImGui.GetContentRegionAvail().X,
            new ResponsiveTableColumn("Submarine", fc.Submarines.Select(sub => sub.Name), 120, 220, Flexible: true),
            new ResponsiveTableColumn("Basis", fc.Submarines.Select(FormatProjectionBasis), 100, 140),
            new ResponsiveTableColumn("Build", fc.Submarines.Select(sub => sub.Build.Code), 60, 100),
            new ResponsiveTableColumn("Route", fc.Submarines.Select(Route), 85, 180),
            new ResponsiveTableColumn("Full cycle", fc.Submarines.Select(sub => FormatProjectionCycle(sub.FullCycleDuration)), 85, 135),
            new ResponsiveTableColumn("Gil/voyage", fc.Submarines.Select(sub => FormatProjectedGil(sub.AverageGilPerVoyage)), 95, 160),
            new ResponsiveTableColumn("Estimated gil", fc.Submarines.Select(sub => FormatProjectedGil(sub.ProjectedGil(horizon))), 110, 185),
            new ResponsiveTableColumn("Samples", fc.Submarines.Select(sub => sub.Sample.ReturnCount.ToString("N0")), 65, 100));
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.SizingFixedFit;
        if (layout.RequiresHorizontalScroll) flags |= ImGuiTableFlags.ScrollX;
        if (!ImGui.BeginTable($"projection-subs-{fc.FcIdKey}", 8, flags,
                new Vector2(-1, CalculateTableHeight(fc.Submarines.Count, layout.RequiresHorizontalScroll)),
                layout.RequiresHorizontalScroll ? layout.InnerWidth : 0f)) return;
        SetupResponsiveTableColumns(layout);
        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableHeadersRow();
        foreach (var sub in fc.Submarines)
        {
            ImGui.TableNextRow();
            DrawTableText(sub.Name);
            DrawIncomeProjectionSubmarineTooltip(sub, horizon);
            ImGui.PushStyleColor(ImGuiCol.Text, sub.IsApproximate || !sub.IsAvailable ? PlannerUi.Amber : PlannerUi.Muted);
            DrawTableText(FormatProjectionBasis(sub));
            ImGui.PopStyleColor();
            DrawIncomeProjectionSubmarineTooltip(sub, horizon);
            ImGui.TableNextColumn(); DrawCurrentBuild(sub.Build);
            DrawTableText(Route(sub));
            PlannerUi.Tooltip(sub.RouteSource switch
            {
                FarmingRouteSource.Pinned => "Pinned farming route",
                FarmingRouteSource.CurrentTrackerRoute => "Current SubmarineTracker route",
                _ => "No farming route is available.",
            });
            DrawTableText(FormatProjectionCycle(sub.FullCycleDuration));
            PlannerUi.Tooltip($"Voyage: {FormatProjectionCycle(sub.VoyageDuration)} · collection delay: {FormatProjectionCycle(sub.CollectionDelay)}");
            DrawTableText(FormatProjectedGil(sub.AverageGilPerVoyage), rightAligned: true);
            DrawIncomeProjectionSubmarineTooltip(sub, horizon);
            DrawTableText(FormatProjectedGil(sub.ProjectedGil(horizon)), rightAligned: true);
            DrawIncomeProjectionSubmarineTooltip(sub, horizon);
            DrawTableText(sub.Sample.ReturnCount.ToString("N0"), rightAligned: true);
            DrawIncomeProjectionSubmarineTooltip(sub, horizon);
        }
        ImGui.EndTable();
    }

    private void DrawIncomeProjectionSubmarineTooltip(IncomeSubmarineProjection sub, IncomeProjectionHorizon horizon)
    {
        if (!ImGui.IsItemHovered()) return;
        PlannerUi.BeginTooltip();
        PlannerUi.WrappedText($"{sub.Name} · R{sub.Rank} · {sub.Build.Code}", PlannerUi.Teal);
        if (sub.UnavailableReason is { } reason) PlannerUi.WrappedText(reason, PlannerUi.Amber);
        PlannerUi.WrappedText(sub.Sample.Source == IncomeProjectionSampleSource.Own
            ? "Source: this submarine's matching history."
            : "Source: matching history pooled across visible FCs, including this submarine.");
        PlannerUi.WrappedText($"Matching returns: {sub.Sample.ReturnCount} · contributing FCs: {sub.Sample.ContributingFcCount}");
        PlannerUi.WrappedText($"Sample dates: {FormatIncomeDate(sub.Sample.FirstReturnAtUtc)} – {FormatIncomeDate(sub.Sample.LastReturnAtUtc)}");
        if (sub.Sample.MatchKind == IncomeProjectionMatchKind.PreviousRank)
        {
            PlannerUi.WrappedText($"Using rank {sub.Sample.ReferenceRank} history for current rank {sub.Rank}.", PlannerUi.Amber);
            PlannerUi.WrappedText("Approximate: recorded stats match today's parts at the previous rank. Historical parts are not reconstructed, and the observed average is used without adjustment.");
        }
        else PlannerUi.WrappedText("Exact stats: history matches current surveillance, retrieval and favor.");
        PlannerUi.WrappedText($"Current stats (surveillance / retrieval / favor): {FormatProjectionStats(sub.CurrentStats)}");
        PlannerUi.WrappedText($"Sample matching stats: {FormatProjectionStats(sub.Sample.MatchedStats)}");
        PlannerUi.WrappedText($"Pooled history: {FormatProjectionMatchCounts(sub)}.");
        if (sub.PreviousRankMatchingReturnCount is null)
            PlannerUi.WrappedText(this.configuration.AllowPreviousRankIncomeApproximations
                ? "Previous-rank matching is unavailable for this setup." : "Previous-rank approximations are disabled.", PlannerUi.Muted);
        PlannerUi.WrappedText("Last 90 days · same sector set. Historical sector order is unknown. Exact and previous-rank samples are not combined.");
        PlannerUi.WrappedText($"Estimated gil/day: {FormatProjectedGil(sub.GilPerDay)} · {(int)horizon} days: {FormatProjectedGil(sub.ProjectedGil(horizon))}");
        PlannerUi.WrappedText("A steady estimate with fractional cycles; current voyage timing and future leveling are not modeled. Sample counts are not a confidence guarantee.", PlannerUi.Muted);
        PlannerUi.EndTooltip();
    }

    private static string FormatProjectionBasis(IncomeSubmarineProjection sub)
        => !sub.IsAvailable ? "Unavailable" : sub.IsApproximate ? "Approximate" : "Exact stats";

    private static string FormatProjectionStats(IncomeProjectionStats? stats)
        => stats is { } value ? $"{value.Surveillance} / {value.Retrieval} / {value.Favor}" : "—";

    private static string FormatProjectionMatchCounts(IncomeSubmarineProjection sub)
        => $"{sub.ExactMatchingReturnCount} / {IncomeProjectionCalculator.MinimumReturns} exact returns" +
            (sub.PreviousRankMatchingReturnCount is { } previous
                ? $" · {previous} / {IncomeProjectionCalculator.MinimumReturns} previous-rank returns" : string.Empty);

    private static string FormatProjectionCycle(TimeSpan? duration)
        => duration is { } value ? $"{value.TotalHours:0.#}h" : "—";

    private static string FormatProjectedGil(decimal? value, bool compact = false)
        => value is not { } gil ? "—" : !compact ? gil.ToString("N0")
            : gil >= 1_000_000_000 ? $"{gil / 1_000_000_000:0.#}b"
            : gil >= 1_000_000 ? $"{gil / 1_000_000:0.#}m"
            : gil >= 1_000 ? $"{gil / 1_000:0.#}k" : gil.ToString("N0");
}
