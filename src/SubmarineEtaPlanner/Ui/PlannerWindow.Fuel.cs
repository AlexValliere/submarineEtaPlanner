using Dalamud.Bindings.ImGui;
using SubmarineEtaPlanner.Planner;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

public sealed partial class PlannerWindow
{
    private void DrawFuelRunway(FcOperationalProjection projection, DateTimeOffset now)
    {
        var forecast = CreateFuelRunwayForecast(projection, now);
        var statusColor = forecast.Status switch
        {
            FuelRunwayStatus.Healthy => PlannerUi.Green,
            FuelRunwayStatus.Low => PlannerUi.Amber,
            FuelRunwayStatus.Critical => PlannerUi.Red,
            _ => PlannerUi.Muted,
        };

        ImGui.PushStyleColor(ImGuiCol.ChildBg, PlannerUi.PanelBackground);
        ImGui.PushStyleColor(ImGuiCol.Border, PlannerUi.Border);
        ImGui.BeginChild($"fuel-runway-{projection.State.FcIdKey}", new Vector2(-1, 0), true);
        ImGui.TextColored(PlannerUi.Teal, "Ceruleum runway");
        ImGui.SameLine();
        PlannerUi.DrawStatusPill(forecast.Status.ToString(), statusColor);
        ImGui.TextColored(PlannerUi.Muted, "Read-only forecast from local stock and active farming schedules.");
        ImGui.Spacing();

        if (ImGui.BeginTable(
                $"fuel-runway-values-{projection.State.FcIdKey}",
                5,
                ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Stock", ImGuiTableColumnFlags.WidthStretch, 1.35f);
            ImGui.TableSetupColumn("Reserve", ImGuiTableColumnFlags.WidthStretch, 0.75f);
            ImGui.TableSetupColumn("Projected use", ImGuiTableColumnFlags.WidthStretch, 0.9f);
            ImGui.TableSetupColumn("Full fleet sends", ImGuiTableColumnFlags.WidthStretch, 0.9f);
            ImGui.TableSetupColumn("Refill before", ImGuiTableColumnFlags.WidthStretch, 1.3f);
            ImGui.TableHeadersRow();
            ImGui.TableNextRow();
            DrawTableText(FormatFuelStock(forecast));
            DrawTableText($"{forecast.Reserve:N0} tanks");
            DrawTableText($"{forecast.TanksPerDay:N1} tanks/day");
            DrawTableText(forecast.Status == FuelRunwayStatus.Unavailable
                ? "Unavailable"
                : forecast.FullFleetSendsRemaining.ToString("N0"));
            DrawTableText(FormatRefillDeadline(forecast, now));
            ImGui.EndTable();
        }

        if (forecast.ApproximateRunway is { } runway && forecast.Status != FuelRunwayStatus.Unavailable)
        {
            ImGui.TextColored(PlannerUi.Muted, $"Approximate runway above reserve: {FormatDuration(runway)}.");
        }

        foreach (var warning in forecast.Warnings)
            ImGui.TextColored(PlannerUi.Amber, $"• {warning}");

        ImGui.EndChild();
        ImGui.PopStyleColor(2);
    }

    private FuelRunwayForecast CreateFuelRunwayForecast(
        FcOperationalProjection projection,
        DateTimeOffset now)
    {
        var preferences = this.configuration.GetFcPreferences(projection.State.FcIdKey);
        var routePlans = FarmingRoutePlanResolver.Resolve(
            projection.State,
            preferences,
            projection.EffectiveTargetRank,
            this.catalog,
            this.operationalCatalog);
        var cycles = FarmingCyclePlanBuilder.Build(
            projection.State,
            routePlans,
            preferences,
            this.configuration.Settings,
            now);
        var stock = projection.State.GameFreeCompanyId is { } freeCompanyId
            ? FuelStockResolver.Resolve(
                freeCompanyId,
                preferences.FuelStockMode,
                preferences.FuelHolderCharacterId,
                preferences.ManualCeruleumTanks.GetValueOrDefault(),
                this.getFuelObservations())
            : new ResolvedFuelStock(
                CeruleumTanks: null,
                Source: null,
                CharacterId: null,
                CharacterName: null,
                World: null,
                ObservedAtUtc: null,
                IsLive: false,
                UnavailableReason: "The free company ID is unavailable, so locally observed fuel stock cannot be matched safely.");

        return FuelRunwayCalculator.Calculate(
            stock,
            cycles,
            routePlans.Count,
            preferences.CeruleumReserve,
            now);
    }

    private static string FormatFuelStock(FuelRunwayForecast forecast)
    {
        if (forecast.StockBasis is not { } stock)
            return "Unavailable";

        return forecast.StockSource switch
        {
            FuelStockSourceKind.LiveCharacter => $"Current · {stock:N0} tanks",
            FuelStockSourceKind.Manual => $"Current (manual) · {stock:N0} tanks",
            FuelStockSourceKind.LastObservedCharacter when forecast.StockObservedAtUtc is { } observedAt =>
                $"Last observed · {stock:N0} · {observedAt.LocalDateTime:g}",
            FuelStockSourceKind.LastObservedCharacter => $"Last observed · {stock:N0} tanks",
            _ => "Unavailable",
        };
    }

    private static string FormatRefillDeadline(FuelRunwayForecast forecast, DateTimeOffset now)
    {
        if (forecast.Status == FuelRunwayStatus.Unavailable)
            return "Unavailable";
        if (forecast.RefillBeforeUtc is not { } refillBefore)
            return forecast.TanksPerDay <= 0 ? "Not required" : "Beyond forecast horizon";
        return $"{refillBefore.LocalDateTime:g} · {FormatRelative(refillBefore, now)}";
    }
}
