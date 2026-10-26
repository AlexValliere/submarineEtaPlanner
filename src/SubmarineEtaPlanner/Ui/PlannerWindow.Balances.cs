using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using SubmarineEtaPlanner.Planner;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

public sealed partial class PlannerWindow
{
    private sealed record BalanceFleet(FcState State, FcGilBalance Balance)
    {
        public bool IsPartial => Balance.WalletGil is null || Balance.ChestGil is null;
        public string RecordedTotal => FormatBalanceGil(Balance.RecordedGil)
            + (Balance.HasAnyObservation && IsPartial ? " · partial" : string.Empty);
    }

    private sealed record BalanceTableRow(string Source, long? Gil, DateTimeOffset? ObservedAtUtc, bool IsLive = false);

    private void DrawGilBalances(EtaPlannerSnapshot currentSnapshot)
    {
        var observations = this.getGilBalances();
        var fleets = VisibleFreeCompanies(currentSnapshot)
            .Where(fc => this.incomeFcScope is null || fc.FcIdKey == this.incomeFcScope)
            .OrderByDescending(fc => this.configuration.GetFcPreferences(fc.FcIdKey).Favorite)
            .ThenBy(fc => fc.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(fc => new BalanceFleet(fc, GilBalanceCalculator.ForFreeCompany(fc.GameFreeCompanyId, observations)))
            .ToArray();
        var observedWallets = fleets.Sum(fc => fc.Balance.Characters.Count);
        var observedChests = fleets.Count(fc => fc.Balance.Chest is not null);
        var observedFleets = fleets.Count(fc => fc.Balance.HasAnyObservation);

        PlannerUi.WrappedText("Last observed balances · locally recorded · excludes retainers", PlannerUi.Muted);
        DrawGilBalanceSummary(fleets, observedWallets, observedChests, observedFleets);
        ImGui.Spacing();
        PlannerUi.WrappedText($"{fleets.Length} visible FC{(fleets.Length == 1 ? string.Empty : "s")} · "
            + $"{observedWallets} recorded wallet{(observedWallets == 1 ? string.Empty : "s")} · "
            + $"{observedChests} of {fleets.Length} chests observed", PlannerUi.Muted);
        PlannerUi.Tooltip("Wallet coverage describes characters observed by this plugin, not every member of an FC. Unknown amounts are excluded from recorded totals.");

        if (observedFleets == 0)
        {
            ImGui.Spacing();
            PlannerUi.Callout("balances-empty", FontAwesomeIcon.Coins, "No FC balances observed yet",
                "Log in on your FC characters and open each FC chest to record its gil. Last observed values remain available after switching characters.",
                PlannerUi.Muted);
        }

        if (fleets.Length > 0)
        {
            ImGui.Spacing();
            var layout = CalculateBalanceHeaderLayout(fleets, ImGui.GetContentRegionAvail().X - FavoriteControlWidth);
            DrawCompactFcHeaderLegend(layout, ["FC tag", "World", "Wallets", "FC chest", "Recorded total"], centerFrom: 2);
            foreach (var fc in fleets)
                DrawGilBalanceFleet(fc, layout);
        }

        // Characters associated with another known FC are never reclassified as unassigned:
        // that FC may be hidden or absent from the current tracker snapshot.
        if (this.incomeFcScope is null)
        {
            var unassigned = GilBalanceCalculator.UnassignedCharacters(observations);
            if (unassigned.Count > 0)
            {
                ImGui.Spacing();
                if (ImGui.CollapsingHeader($"{unassigned.Count} unassigned character{(unassigned.Count == 1 ? string.Empty : "s")}##balance-unassigned"))
                {
                    PlannerUi.WrappedText("No confirmed FC association. These wallets are excluded from FC totals.", PlannerUi.Muted);
                    DrawGilBalanceTable("unassigned", unassigned.Select(CharacterBalanceRow).ToArray());
                }
            }
        }
        ImGui.Spacing();
        PlannerUi.WrappedText("Last observed snapshots; balances may have changed. Wallets update while logged in; FC chest gil updates when the chest is open and ready. Totals combine observations taken at different times.", PlannerUi.Muted);
    }

    private void DrawGilBalanceSummary(IReadOnlyList<BalanceFleet> fleets, int wallets, int chests, int observedFleets)
    {
        var walletGil = SumKnownBalances(fleets.Select(fc => fc.Balance.WalletGil));
        var chestGil = SumKnownBalances(fleets.Select(fc => fc.Balance.ChestGil));
        var totalGil = SumKnownBalances(fleets.Select(fc => fc.Balance.RecordedGil));
        var partial = observedFleets < fleets.Count || fleets.Any(fc => fc.IsPartial);
        var columns = ImGui.GetContentRegionAvail().X < 680f * ImGuiHelpers.GlobalScale ? 2 : 3;
        if (!ImGui.BeginTable("gil-balance-summary", columns, ImGuiTableFlags.SizingStretchSame))
            return;
        ImGui.TableNextColumn();
        PlannerUi.MetricCard(this.typography, "balance-wallet-total", FontAwesomeIcon.Coins,
            FormatBalanceGil(walletGil, compact: true), $"Character wallets · {wallets} observed", PlannerUi.Green);
        ImGui.TableNextColumn();
        PlannerUi.MetricCard(this.typography, "balance-chest-total", FontAwesomeIcon.Building,
            FormatBalanceGil(chestGil, compact: true), $"FC chests · {chests} of {fleets.Count} observed", PlannerUi.Teal);
        ImGui.TableNextColumn();
        PlannerUi.MetricCard(this.typography, "balance-recorded-total", FontAwesomeIcon.Coins,
            FormatBalanceGil(totalGil, compact: true), "Known recorded gil" + (partial ? " · partial coverage" : string.Empty),
            partial ? PlannerUi.Amber : PlannerUi.Cyan);
        ImGui.EndTable();
    }

    private static long? SumKnownBalances(IEnumerable<long?> values)
    {
        var known = values.Where(value => value.HasValue).ToArray();
        return known.Length == 0 ? null : known.Sum(value => value!.Value);
    }

    private static CompactFcHeaderLayout CalculateBalanceHeaderLayout(IReadOnlyList<BalanceFleet> fleets, float availableWidth)
        => CalculateCompactFcHeaderLayout(
            [MeasureHeaderColumn(fleets.Select(fc => fc.State.FreeCompanyTag), "FC tag", 70f, 135f),
             MeasureHeaderColumn(fleets.Select(fc => fc.State.World), "World", 90f, 150f),
             MeasureHeaderColumn(fleets.Select(fc => FormatBalanceGil(fc.Balance.WalletGil)), "Wallets", 100f, 170f),
             MeasureHeaderColumn(fleets.Select(fc => FormatBalanceGil(fc.Balance.ChestGil)), "FC chest", 100f, 170f),
             MeasureHeaderColumn(fleets.Select(fc => fc.RecordedTotal), "Recorded total", 130f, 210f)],
            [0f, 0f, 1f, 1f, 1f], 2, availableWidth);

    private void DrawGilBalanceFleet(BalanceFleet fleet, CompactFcHeaderLayout layout)
    {
        var fc = fleet.State;
        var balance = fleet.Balance;
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
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, PlannerTheme.Hover);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, PlannerTheme.Selected);
        var open = ImGui.CollapsingHeader($"###balance-fc-{fc.FcIdKey}");
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();
        var text = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        DrawCompactFcHeaderCell(origin, layout, 0, fc.FreeCompanyTag, text);
        DrawCompactFcHeaderCell(origin, layout, 1, string.IsNullOrWhiteSpace(fc.World) ? "—" : fc.World, text);
        DrawCompactFcHeaderCell(origin, layout, 2, FormatBalanceGil(balance.WalletGil), balance.WalletGil is null ? PlannerUi.Muted : text, centered: true);
        DrawCompactFcHeaderCell(origin, layout, 3, FormatBalanceGil(balance.ChestGil), balance.ChestGil is null ? PlannerUi.Muted : text, centered: true);
        DrawCompactFcHeaderCell(origin, layout, 4, fleet.RecordedTotal,
            !balance.HasAnyObservation ? PlannerUi.Muted : fleet.IsPartial ? PlannerUi.Amber : text, centered: true);
        if (ImGui.IsItemHovered())
        {
            PlannerUi.BeginTooltip();
            PlannerUi.WrappedText(fc.DisplayName, PlannerUi.Teal);
            PlannerUi.WrappedText($"Character wallets: {FormatBalanceGil(balance.WalletGil)} gil · {balance.Characters.Count} observed");
            PlannerUi.WrappedText($"FC chest: {FormatBalanceGil(balance.ChestGil)} gil");
            PlannerUi.WrappedText("Recorded total combines last observed balances. Unknown amounts and unobserved characters are excluded.", PlannerUi.Muted);
            PlannerUi.EndTooltip();
        }
        if (!open)
            return;

        ImGui.Spacing();
        DrawFcShortcuts(fc.FcIdKey);
        if (fc.GameFreeCompanyId is null)
            PlannerUi.WrappedText("The tracker's numeric FC ID could not be decoded, so local balances cannot be matched to this FC.", PlannerUi.Amber);
        else if (fleet.IsPartial)
            PlannerUi.WrappedText("Partial coverage · log in on this FC's characters and open its chest to record missing balances.", PlannerUi.Muted);
        var rows = balance.Characters.Select(CharacterBalanceRow)
            .Append(new BalanceTableRow("FC chest", balance.ChestGil, balance.Chest?.ObservedAtUtc))
            .ToArray();
        DrawGilBalanceTable(fc.FcIdKey, rows);
        if (balance.OldestWalletObservationAtUtc is { } oldest)
            PlannerUi.WrappedText($"Oldest wallet observation: {FormatIncomeDate(oldest)}", PlannerUi.Muted);
    }

    private static BalanceTableRow CharacterBalanceRow(CharacterGilObservation observation)
        => new(string.IsNullOrWhiteSpace(observation.World)
                ? observation.CharacterName : $"{observation.CharacterName} @ {observation.World}",
            observation.Gil, observation.ObservedAtUtc, observation.IsLive);

    private static void DrawGilBalanceTable(string id, IReadOnlyList<BalanceTableRow> rows)
    {
        var layout = CalculateResponsiveTableLayout(ImGui.GetContentRegionAvail().X,
            new ResponsiveTableColumn("Character / source", rows.Select(row => row.Source), 150, 310, Flexible: true, FillRemaining: true),
            new ResponsiveTableColumn("Gil", rows.Select(row => FormatBalanceGil(row.Gil)), 100, 170),
            new ResponsiveTableColumn("Last observed", rows.Select(FormatBalanceObservation), 150, 230));
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.SizingFixedFit;
        if (layout.RequiresHorizontalScroll)
            flags |= ImGuiTableFlags.ScrollX;
        if (!ImGui.BeginTable($"gil-balances-{id}", 3, flags,
            new Vector2(-1, CalculateTableHeight(rows.Count, layout.RequiresHorizontalScroll)),
            layout.RequiresHorizontalScroll ? layout.InnerWidth : 0f))
            return;
        SetupResponsiveTableColumns(layout);
        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableHeadersRow();
        foreach (var row in rows)
        {
            ImGui.TableNextRow();
            DrawTableText(row.Source);
            PlannerUi.Tooltip(row.Source);
            if (row.Gil is null) ImGui.PushStyleColor(ImGuiCol.Text, PlannerUi.Muted);
            DrawTableText(FormatBalanceGil(row.Gil), rightAligned: true);
            if (row.Gil is null) ImGui.PopStyleColor();
            DrawTableText(FormatBalanceObservation(row));
            if (row.ObservedAtUtc is { } observedAt)
                PlannerUi.Tooltip(observedAt.LocalDateTime.ToString("F") + (row.IsLive ? " · current character" : string.Empty));
        }
        ImGui.EndTable();
    }

    private static string FormatBalanceObservation(BalanceTableRow row)
        => row.ObservedAtUtc is null ? "Not observed" : FormatIncomeDate(row.ObservedAtUtc) + (row.IsLive ? " · current" : string.Empty);

    private static string FormatBalanceGil(long? value, bool compact = false)
        => value is null ? "—" : compact ? ResultsViewState.FormatCompactGil(value.Value) : value.Value.ToString("N0");
}
