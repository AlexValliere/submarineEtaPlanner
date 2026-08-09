using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class FuelRunwayCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LiveStockWithFourSynchronizedSubmarinesUsesGroupedFleetDebits()
    {
        var cycles = Enumerable.Range(1, 4)
            .Select(id => Cycle(id, 10, TimeSpan.FromDays(1), Now.AddDays(1)))
            .ToArray();

        var forecast = Calculate(LiveStock(100), cycles);

        Assert.Equal(FuelStockUsability.Current, forecast.StockUsability);
        Assert.Equal(40, forecast.Reserve);
        Assert.Equal(40, forecast.TanksPerDay, 8);
        Assert.Equal(1, forecast.FullFleetSendsRemaining);
        Assert.Equal(Now.AddDays(2), forecast.RefillBeforeUtc);
        Assert.Equal(FuelRunwayStatus.Low, forecast.Status);
    }

    [Fact]
    public void ManualStockIsCurrentWithoutAnObservationTimestamp()
    {
        var forecast = Calculate(ManualStock(30), [Cycle(1, 10, TimeSpan.FromDays(1), Now.AddDays(1))]);

        Assert.Equal(30, forecast.StockBasis);
        Assert.Null(forecast.StockObservedAtUtc);
        Assert.Equal(FuelStockSourceKind.Manual, forecast.StockSource);
        Assert.Equal(FuelStockUsability.Current, forecast.StockUsability);
        Assert.Equal(2, forecast.FullFleetSendsRemaining);
    }

    [Fact]
    public void LastObservedStockAfterEveryCurrentDepartureIsScheduleCompatible()
    {
        var observedAt = Now.AddHours(-1);
        var cycles = new[]
        {
            Cycle(1, 10, TimeSpan.FromDays(1), Now.AddHours(3), observedAt.AddHours(-1)),
            Cycle(2, 10, TimeSpan.FromDays(1), Now.AddHours(5), observedAt),
        };

        var forecast = Calculate(LastObservedStock(50, observedAt), cycles);

        Assert.Equal(FuelStockUsability.LastObservedScheduleCompatible, forecast.StockUsability);
        Assert.Equal(FuelRunwayStatus.Low, forecast.Status);
        Assert.DoesNotContain(forecast.Warnings, warning => warning.Contains("refresh it", StringComparison.Ordinal));
    }

    [Fact]
    public void LastObservedStockBeforeOneCurrentDepartureIsStale()
    {
        var observedAt = Now.AddHours(-4);
        var cycles = new[]
        {
            Cycle(1, 10, TimeSpan.FromDays(1), Now.AddHours(3), observedAt.AddHours(-1)),
            Cycle(2, 10, TimeSpan.FromDays(1), Now.AddHours(5), observedAt.AddHours(1)),
        };

        var forecast = Calculate(LastObservedStock(50, observedAt), cycles);

        Assert.Equal(FuelStockUsability.StaleAfterKnownDeparture, forecast.StockUsability);
        Assert.Equal(FuelRunwayStatus.Unavailable, forecast.Status);
        Assert.Null(forecast.RefillBeforeUtc);
        Assert.Contains(forecast.Warnings, warning => warning.Contains("Log into the fuel-holder character", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingCurrentDepartureDataMakesLastObservationUnavailable()
    {
        var cycle = Cycle(1, 10, TimeSpan.FromDays(1), Now.AddHours(2)) with
        {
            CurrentVoyageAlreadyPaid = true,
            CurrentVoyageDepartureAtUtc = null,
        };

        var forecast = Calculate(LastObservedStock(50, Now.AddHours(-1)), [cycle]);

        Assert.Equal(FuelStockUsability.Unavailable, forecast.StockUsability);
        Assert.Equal(FuelRunwayStatus.Unavailable, forecast.Status);
        Assert.Null(forecast.RefillBeforeUtc);
    }

    [Fact]
    public void IdleSubmarineRequiresFuelImmediately()
    {
        var forecast = Calculate(
            LiveStock(15),
            [Cycle(1, 10, TimeSpan.FromDays(1), Now)],
            fixedReserve: 10);

        Assert.Equal(Now, forecast.RefillBeforeUtc);
        Assert.Equal(FuelRunwayStatus.Critical, forecast.Status);
    }

    [Fact]
    public void ReadyToCollectSubmarineUsesAnImmediateNextDeparture()
    {
        var forecast = Calculate(
            LiveStock(20),
            [Cycle(1, 10, TimeSpan.FromDays(1), Now, Now.AddDays(-1))],
            fixedReserve: 15);

        Assert.Equal(Now, forecast.RefillBeforeUtc);
        Assert.Equal(FuelRunwayStatus.Critical, forecast.Status);
    }

    [Fact]
    public void CurrentUnderwayVoyageIsNotChargedTwice()
    {
        var forecast = Calculate(
            LiveStock(25),
            [Cycle(1, 10, TimeSpan.FromDays(1), Now.AddDays(1), Now.AddHours(-2))],
            fixedReserve: 10);

        Assert.Equal(Now.AddDays(2), forecast.RefillBeforeUtc);
        Assert.Equal(1, forecast.FullFleetSendsRemaining);
    }

    [Fact]
    public void SameTimestampDeparturesAreEvaluatedAsOneGroup()
    {
        var departure = Now.AddHours(2);
        var forecast = Calculate(
            LiveStock(30),
            [
                Cycle(1, 8, TimeSpan.FromDays(1), departure),
                Cycle(2, 8, TimeSpan.FromDays(1), departure),
            ],
            fixedReserve: 15);

        Assert.Equal(departure, forecast.RefillBeforeUtc);
        Assert.Equal(FuelRunwayStatus.Critical, forecast.Status);
    }

    [Fact]
    public void ExplicitReserveOverridesAutomaticFullDispatchReserve()
    {
        var forecast = Calculate(
            LiveStock(100),
            [Cycle(1, 10, TimeSpan.FromDays(1), Now.AddDays(1))],
            fixedReserve: 35);

        Assert.Equal(35, forecast.Reserve);
        Assert.Equal(6, forecast.FullFleetSendsRemaining);
    }

    [Fact]
    public void AutomaticReserveIsOneCompleteDispatchOfAllFarmingSubmarines()
    {
        var forecast = Calculate(
            LiveStock(100),
            [
                Cycle(1, 7, TimeSpan.FromDays(1), Now.AddDays(1)),
                Cycle(2, 13, TimeSpan.FromDays(1), Now.AddDays(1)),
            ]);

        Assert.Equal(20, forecast.Reserve);
        Assert.Equal(4, forecast.FullFleetSendsRemaining);
    }

    [Fact]
    public void AlreadyBelowReserveIsCriticalNow()
    {
        var forecast = Calculate(
            LiveStock(9),
            [Cycle(1, 10, TimeSpan.FromDays(1), Now.AddDays(1))],
            fixedReserve: 10);

        Assert.Equal(Now, forecast.RefillBeforeUtc);
        Assert.Equal(TimeSpan.Zero, forecast.ApproximateRunway);
        Assert.Equal(FuelRunwayStatus.Critical, forecast.Status);
    }

    [Fact]
    public void ZeroStockIsAvailableAndCriticalInsteadOfUnknown()
    {
        var forecast = Calculate(LiveStock(0), [Cycle(1, 10, TimeSpan.FromDays(1), Now)]);

        Assert.Equal(0, forecast.StockBasis);
        Assert.Equal(FuelStockUsability.Current, forecast.StockUsability);
        Assert.Equal(FuelRunwayStatus.Critical, forecast.Status);
    }

    [Fact]
    public void NoFarmingSubmarinesHasNoFuelDeadline()
    {
        var forecast = Calculate(LiveStock(100), [], activeCount: 0);

        Assert.Equal(0, forecast.Reserve);
        Assert.Equal(0, forecast.TanksPerDay);
        Assert.Equal(0, forecast.FullFleetSendsRemaining);
        Assert.Null(forecast.RefillBeforeUtc);
        Assert.Null(forecast.ApproximateRunway);
        Assert.Equal(FuelRunwayStatus.Healthy, forecast.Status);
    }

    [Fact]
    public void MissingRouteMakesStockBasedForecastUnavailable()
    {
        var forecast = Calculate(LiveStock(100), [], activeCount: 1);

        Assert.Equal(FuelStockUsability.Current, forecast.StockUsability);
        Assert.Equal(FuelRunwayStatus.Unavailable, forecast.Status);
        Assert.Null(forecast.RefillBeforeUtc);
        Assert.Contains(forecast.Warnings, warning => warning.Contains("complete route", StringComparison.Ordinal));
    }

    [Fact]
    public void DifferentVoyageDurationsContributeTheirOwnDailyRates()
    {
        var forecast = Calculate(
            LiveStock(100),
            [
                Cycle(1, 10, TimeSpan.FromHours(12), Now.AddHours(6)),
                Cycle(2, 12, TimeSpan.FromDays(2), Now.AddHours(8)),
            ]);

        Assert.Equal(26, forecast.TanksPerDay, 8);
    }

    [Fact]
    public void InputOrderingDoesNotChangeTheResult()
    {
        var cycles = new[]
        {
            Cycle(3, 7, TimeSpan.FromHours(18), Now.AddHours(2)),
            Cycle(1, 5, TimeSpan.FromHours(12), Now.AddHours(1)),
            Cycle(2, 6, TimeSpan.FromHours(15), Now.AddHours(3)),
        };

        var forward = Calculate(LiveStock(100), cycles);
        var reverse = Calculate(LiveStock(100), cycles.Reverse().ToArray());

        Assert.Equal(forward, reverse);
    }

    [Fact]
    public void DefensiveSimulationHorizonSuppressesUnsupportedPreciseDeadline()
    {
        var forecast = Calculate(
            LiveStock(1_000_000),
            [Cycle(1, 1, TimeSpan.FromDays(365), Now.AddDays(365))],
            fixedReserve: 1);

        Assert.Null(forecast.RefillBeforeUtc);
        Assert.Equal(FuelRunwayStatus.Healthy, forecast.Status);
        Assert.Contains(forecast.Warnings, warning => warning.Contains("simulation horizon", StringComparison.Ordinal));
    }

    [Fact]
    public void TanksPerDayRemainsAvailableWhenInventoryIsStale()
    {
        var observedAt = Now.AddDays(-2);
        var forecast = Calculate(
            LastObservedStock(100, observedAt),
            [Cycle(1, 12, TimeSpan.FromHours(12), Now.AddHours(2), observedAt.AddHours(1))]);

        Assert.Equal(24, forecast.TanksPerDay, 8);
        Assert.Equal(FuelRunwayStatus.Unavailable, forecast.Status);
    }

    [Fact]
    public void StaleStockNeverProducesAPreciseRefillDate()
    {
        var observedAt = Now.AddDays(-2);
        var forecast = Calculate(
            LastObservedStock(10, observedAt),
            [Cycle(1, 10, TimeSpan.FromDays(1), Now, observedAt.AddHours(1))]);

        Assert.Null(forecast.RefillBeforeUtc);
        Assert.Null(forecast.ApproximateRunway);
        Assert.Equal(0, forecast.FullFleetSendsRemaining);
    }

    [Fact]
    public void UnavailableInventoryDoesNotHideScheduleOnlyConsumptionRate()
    {
        var stock = new ResolvedFuelStock(null, null, null, null, null, null, false, "No stock observation exists.");

        var forecast = Calculate(stock, [Cycle(1, 6, TimeSpan.FromHours(12), Now.AddHours(1))]);

        Assert.Null(forecast.StockBasis);
        Assert.Equal(12, forecast.TanksPerDay, 8);
        Assert.Equal(FuelStockUsability.Unavailable, forecast.StockUsability);
        Assert.Equal(FuelRunwayStatus.Unavailable, forecast.Status);
    }

    private static FuelRunwayForecast Calculate(
        ResolvedFuelStock stock,
        IReadOnlyList<FarmingCyclePlan> cycles,
        int? fixedReserve = null,
        int? activeCount = null)
        => FuelRunwayCalculator.Calculate(
            stock,
            cycles,
            activeCount ?? cycles.Count,
            fixedReserve,
            Now);

    private static FarmingCyclePlan Cycle(
        long submarineId,
        int tanks,
        TimeSpan fullCycleDuration,
        DateTimeOffset nextDepartureAtUtc,
        DateTimeOffset? currentDepartureAtUtc = null)
        => new(
            submarineId,
            $"Sub {submarineId}",
            [(uint)submarineId],
            tanks,
            fullCycleDuration,
            TimeSpan.Zero,
            fullCycleDuration,
            nextDepartureAtUtc,
            currentDepartureAtUtc,
            CurrentVoyageAlreadyPaid: currentDepartureAtUtc is not null,
            FarmingRouteSource.Pinned);

    private static ResolvedFuelStock LiveStock(int tanks)
        => new(
            tanks,
            FuelStockSourceKind.LiveCharacter,
            1,
            "Holder",
            "World",
            Now,
            IsLive: true,
            UnavailableReason: null);

    private static ResolvedFuelStock ManualStock(int tanks)
        => new(
            tanks,
            FuelStockSourceKind.Manual,
            null,
            null,
            null,
            null,
            IsLive: true,
            UnavailableReason: null);

    private static ResolvedFuelStock LastObservedStock(int tanks, DateTimeOffset observedAtUtc)
        => new(
            tanks,
            FuelStockSourceKind.LastObservedCharacter,
            1,
            "Holder",
            "World",
            observedAtUtc,
            IsLive: false,
            UnavailableReason: null);
}
