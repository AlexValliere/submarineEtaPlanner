using Dalamud.Bindings.ImGui;
using SubmarineEtaPlanner.Planner;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

public sealed partial class PlannerWindow
{
    private readonly PresentationCache<FleetFuelPresentation> fuelPresentationCache = new();

    private FleetFuelPresentation GetFuelPresentation(FcOperationalProjection projection, DateTimeOffset now)
    {
        var preferences = this.configuration.GetFcPreferences(projection.State.FcIdKey);
        var stock = ResolveFuelStock(projection.State, preferences);
        var key = FuelPresentationFingerprint.Create(projection.State, preferences, projection.EffectiveTargetRank,
            this.configuration.Settings.CollectionDelayMinutes, stock);
        return this.fuelPresentationCache.Get(projection.State.FcIdKey, key, now, () =>
        {
            var routes = FarmingRoutePlanResolver.Resolve(projection.State, preferences, projection.EffectiveTargetRank,
                this.catalog, this.operationalCatalog);
            var cycles = FarmingCyclePlanBuilder.Build(projection.State, routes, preferences, this.configuration.Settings, now);
            var forecast = FuelRunwayCalculator.Calculate(stock, cycles, routes.Count, preferences.CeruleumReserve, now);
            var boundary = projection.State.Submarines.Select(sub => sub.ReturnAtUtc)
                .Concat(cycles.Select(cycle => cycle.NextDepartureAtUtc))
                .Where(value => value > now).Select(value => (DateTimeOffset?)value).Min();
            return (new FleetFuelPresentation(stock, routes, cycles, forecast), boundary);
        });
    }

    private static string CompactFuelLabel(FleetFuelPresentation fuel, bool shortLabel = false)
    {
        if (!fuel.HasFarming) return "Fuel: —";
        var forecast = fuel.Forecast;
        if (forecast.Status == FuelRunwayStatus.Unavailable) return shortLabel ? "Fuel unavailable" : $"Fuel unavailable · {fuel.UnavailableReason}";
        var refill = forecast.RefillBeforeUtc is { } time ? $" · refill before {time.LocalDateTime:ddd d MMM HH:mm}" : "";
        return $"{(forecast.Status == FuelRunwayStatus.Healthy ? "Fuel" : $"Fuel {forecast.Status.ToString().ToLowerInvariant()}")}: {forecast.FullFleetSendsRemaining} sends" +
            (shortLabel ? "" : refill);
    }

    private static System.Numerics.Vector4 FuelStatusColor(FleetFuelPresentation fuel)
        => fuel.Forecast.Status switch
        {
            FuelRunwayStatus.Critical => PlannerUi.Red,
            FuelRunwayStatus.Low => PlannerUi.Amber,
            FuelRunwayStatus.Unavailable => PlannerUi.Muted,
            _ => PlannerUi.Muted,
        };

    private void DrawFuelRunway(FcOperationalProjection projection, DateTimeOffset now)
    {
        var fuel = GetFuelPresentation(projection, now);
        if (!fuel.HasFarming) return;
        var open = ImGui.CollapsingHeader($"Fuel details##fuel-details-{projection.State.FcIdKey}");
        PlannerUi.WrappedText(CompactFuelLabel(fuel), FuelStatusColor(fuel));
        if (!open) return;
        if (ImGui.SmallButton($"Fuel setup##fuel-setup-{projection.State.FcIdKey}"))
            RequestFcNavigation(projection.State.FcIdKey, PlannerPage.FcSetup, fuel: true);
        var forecast = fuel.Forecast;
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
            PlannerUi.WrappedText($"• {warning}", PlannerUi.Amber);

        EndContentPanel();
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
