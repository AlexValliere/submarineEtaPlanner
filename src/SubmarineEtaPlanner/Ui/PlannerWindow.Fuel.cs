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

        BeginContentPanel($"fuel-runway-{projection.State.FcIdKey}");
        ImGui.TextColored(PlannerUi.Teal, "Fuel forecast");
        ImGui.SameLine();
        PlannerUi.DrawStatusPill(forecast.Status.ToString(), statusColor);
        ImGui.TextColored(PlannerUi.Muted, "Read-only forecast from local stock and active farming schedules.");
        ImGui.Spacing();

        var fuelLayout = CalculateResponsiveTableLayout(
            ImGui.GetContentRegionAvail().X,
            new ResponsiveTableColumn("Stock", [FormatFuelStock(forecast)], 150, 330, Flexible: true, FlexWeight: 1.35f),
            new ResponsiveTableColumn("Safety stock", [$"{forecast.Reserve:N0} tanks"], 105, 155),
            new ResponsiveTableColumn("Projected use", [$"{forecast.TanksPerDay:N1} tanks/day"], 110, 170),
            new ResponsiveTableColumn("Fleet trips left", [forecast.Status == FuelRunwayStatus.Unavailable ? "Unavailable" : forecast.FullFleetSendsRemaining.ToString("N0")], 115, 165),
            new ResponsiveTableColumn("Refill before", [FormatRefillDeadline(forecast, now)], 155, 310, Flexible: true, FlexWeight: 1.3f));
        var fuelTableFlags = ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit;
        if (fuelLayout.RequiresHorizontalScroll)
            fuelTableFlags |= ImGuiTableFlags.ScrollX;
        if (ImGui.BeginTable(
                $"fuel-runway-values-{projection.State.FcIdKey}",
                5,
                fuelTableFlags,
                new Vector2(-1, fuelLayout.RequiresHorizontalScroll ? CalculateTableHeight(1, true) : 0f),
                fuelLayout.RequiresHorizontalScroll ? fuelLayout.InnerWidth : 0f))
        {
            SetupResponsiveTableColumns(fuelLayout);
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            ImGui.TableNextColumn(); ImGui.TextUnformatted("Stock");
            ImGui.TableNextColumn(); ImGui.TextUnformatted("Safety stock");
            PlannerUi.Tooltip("Tanks intentionally kept available. Automatic safety stock equals one complete resend of every active farming submarine.");
            ImGui.TableNextColumn(); ImGui.TextUnformatted("Projected use");
            ImGui.TableNextColumn(); ImGui.TextUnformatted("Fleet trips left");
            PlannerUi.Tooltip("Complete fleet-wide farming trips available before the safety stock would be used.");
            ImGui.TableNextColumn(); ImGui.TextUnformatted("Refill before");
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
            ImGui.TextColored(PlannerUi.Muted, $"Approximate time above safety stock: {FormatDuration(runway)}.");
        }

        foreach (var warning in forecast.Warnings)
            ImGui.TextColored(PlannerUi.Amber, $"• {warning}");

        EndContentPanel();
    }

    private FuelRunwayForecast CreateFuelRunwayForecast(
        FcOperationalProjection projection,
        DateTimeOffset now)
    {
        var preferences = this.configuration.GetFcPreferences(projection.State.FcIdKey);
        var stock = ResolveFuelStock(projection.State, preferences);
        return CalculateFuelRunway(
            projection.State,
            projection.EffectiveTargetRank,
            preferences,
            stock,
            now);
    }

    private ResolvedFuelStock ResolveFuelStock(FcState freeCompany, FcPreferences preferences)
        => FuelStockResolver.Resolve(
            freeCompany.GameFreeCompanyId,
            preferences.FuelStockMode,
            preferences.FuelHolderCharacterId,
            preferences.ManualCeruleumTanks.GetValueOrDefault(),
            this.getFuelObservations());

    private FuelRunwayForecast CalculateFuelRunway(
        FcState freeCompany,
        int effectiveTargetRank,
        FcPreferences preferences,
        ResolvedFuelStock stock,
        DateTimeOffset now)
    {
        var routePlans = FarmingRoutePlanResolver.Resolve(
            freeCompany,
            preferences,
            effectiveTargetRank,
            this.catalog,
            this.operationalCatalog);
        var cycles = FarmingCyclePlanBuilder.Build(
            freeCompany,
            routePlans,
            preferences,
            this.configuration.Settings,
            now);

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
            FuelStockSourceKind.Manual => $"Manual · {stock:N0} tanks",
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
