namespace SubmarineEtaPlanner.Planner;

public enum FuelRunwayStatus
{
    Healthy,
    Low,
    Critical,
    Unavailable,
}

public enum FuelStockUsability
{
    Current,
    LastObservedScheduleCompatible,
    StaleAfterKnownDeparture,
    Unavailable,
}

public sealed record FuelRunwayForecast(
    int? StockBasis,
    DateTimeOffset? StockObservedAtUtc,
    FuelStockSourceKind? StockSource,
    FuelStockUsability StockUsability,
    int Reserve,
    double TanksPerDay,
    int FullFleetSendsRemaining,
    DateTimeOffset? RefillBeforeUtc,
    TimeSpan? ApproximateRunway,
    FuelRunwayStatus Status,
    IReadOnlyList<string> Warnings);

public static class FuelRunwayCalculator
{
    public static readonly TimeSpan LowRunwayThreshold = TimeSpan.FromDays(7);
    public static readonly TimeSpan DefensiveSimulationHorizon = TimeSpan.FromDays(3650);

    private const int MaximumDepartureGroups = 100_000;
    private const string StaleObservationWarning =
        "This tank count was observed before a currently tracked voyage departed. " +
        "Log into the fuel-holder character or enter a manual count to refresh it.";

    public static FuelRunwayForecast Calculate(
        ResolvedFuelStock stock,
        IReadOnlyList<FarmingCyclePlan> farmingCycles,
        int activeFarmingSubmarineCount,
        int? fixedReserve,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(farmingCycles);
        if (activeFarmingSubmarineCount < 0)
            throw new ArgumentOutOfRangeException(nameof(activeFarmingSubmarineCount));
        if (fixedReserve is < 0)
            throw new ArgumentOutOfRangeException(nameof(fixedReserve));

        var nowUtc = now.ToUniversalTime();
        var warnings = new List<string>();
        var cycles = farmingCycles
            .OrderBy(cycle => cycle.SubmarineId)
            .ToArray();
        var distinctCycleCount = cycles.Select(cycle => cycle.SubmarineId).Distinct().Count();
        var cyclesAreComplete =
            cycles.Length == distinctCycleCount &&
            cycles.All(cycle => cycle.TanksPerVoyage > 0 && cycle.FullCycleDuration > TimeSpan.Zero) &&
            distinctCycleCount == activeFarmingSubmarineCount;

        if (!cyclesAreComplete)
        {
            warnings.Add(
                "Fuel runway is unavailable because one or more active farming submarines do not have complete route, fuel, and duration data.");
        }

        var usableCycles = cycles
            .Where(cycle => cycle.TanksPerVoyage > 0 && cycle.FullCycleDuration > TimeSpan.Zero)
            .GroupBy(cycle => cycle.SubmarineId)
            .Select(group => group.First())
            .ToArray();
        var tanksPerDay = usableCycles.Sum(cycle =>
            cycle.TanksPerVoyage / cycle.FullCycleDuration.TotalDays);
        var fullDispatchTanks = usableCycles.Sum(cycle => checked((long)cycle.TanksPerVoyage));
        var automaticReserve = checked((int)fullDispatchTanks);
        var reserve = fixedReserve ?? automaticReserve;

        if (activeFarmingSubmarineCount == 0)
            warnings.Add("No active farming submarines consume ceruleum tanks.");

        var stockUsability = EvaluateStockUsability(stock, cycles, cyclesAreComplete, warnings);
        var stockCanDriveForecast = stock.IsAvailable &&
                                    stock.CeruleumTanks is not null &&
                                    stockUsability is FuelStockUsability.Current or
                                        FuelStockUsability.LastObservedScheduleCompatible;
        if (!stockCanDriveForecast || !cyclesAreComplete)
        {
            return CreateUnavailableForecast(
                stock,
                stockUsability,
                reserve,
                tanksPerDay,
                warnings);
        }

        var stockBasis = stock.CeruleumTanks!.Value;
        var fullFleetSendsRemaining = fullDispatchTanks == 0
            ? 0
            : checked((int)Math.Max(0, ((long)stockBasis - reserve) / fullDispatchTanks));
        var approximateRunway = CalculateApproximateRunway(stockBasis, reserve, tanksPerDay, warnings);

        if (stockBasis < reserve)
        {
            warnings.Add("Ceruleum stock is already below the configured reserve.");
            return new FuelRunwayForecast(
                stockBasis,
                stock.ObservedAtUtc?.ToUniversalTime(),
                stock.Source,
                stockUsability,
                reserve,
                tanksPerDay,
                fullFleetSendsRemaining,
                nowUtc,
                TimeSpan.Zero,
                FuelRunwayStatus.Critical,
                warnings.ToArray());
        }

        var simulation = Simulate(stockBasis, reserve, usableCycles, nowUtc);
        if (simulation.HorizonExceeded)
        {
            warnings.Add(
                $"The precise refill deadline is beyond the defensive {DefensiveSimulationHorizon.TotalDays:N0}-day simulation horizon.");
        }

        var status = DetermineStatus(simulation.RefillBeforeUtc, simulation.FirstDepartureAtUtc, nowUtc);
        return new FuelRunwayForecast(
            stockBasis,
            stock.ObservedAtUtc?.ToUniversalTime(),
            stock.Source,
            stockUsability,
            reserve,
            tanksPerDay,
            fullFleetSendsRemaining,
            simulation.RefillBeforeUtc,
            approximateRunway,
            status,
            warnings.ToArray());
    }

    private static FuelStockUsability EvaluateStockUsability(
        ResolvedFuelStock stock,
        IReadOnlyList<FarmingCyclePlan> cycles,
        bool cyclesAreComplete,
        ICollection<string> warnings)
    {
        if (!stock.IsAvailable || stock.CeruleumTanks is null || stock.Source is null)
        {
            warnings.Add(stock.UnavailableReason ?? "Ceruleum stock is unavailable.");
            return FuelStockUsability.Unavailable;
        }

        if (stock.Source is FuelStockSourceKind.LiveCharacter or FuelStockSourceKind.Manual)
            return FuelStockUsability.Current;

        if (stock.Source != FuelStockSourceKind.LastObservedCharacter || stock.ObservedAtUtc is null)
        {
            warnings.Add("The locally observed tank count has no usable observation timestamp.");
            return FuelStockUsability.Unavailable;
        }

        if (!cyclesAreComplete || cycles.Any(cycle =>
                cycle.CurrentVoyageAlreadyPaid && cycle.CurrentVoyageDepartureAtUtc is null))
        {
            warnings.Add(
                "The locally observed tank count cannot be checked against every current farming voyage departure.");
            return FuelStockUsability.Unavailable;
        }

        var observedAtUtc = stock.ObservedAtUtc.Value.ToUniversalTime();
        if (cycles.Any(cycle =>
                cycle.CurrentVoyageDepartureAtUtc is { } departure &&
                departure.ToUniversalTime() > observedAtUtc))
        {
            warnings.Add(StaleObservationWarning);
            return FuelStockUsability.StaleAfterKnownDeparture;
        }

        return FuelStockUsability.LastObservedScheduleCompatible;
    }

    private static FuelRunwayForecast CreateUnavailableForecast(
        ResolvedFuelStock stock,
        FuelStockUsability stockUsability,
        int reserve,
        double tanksPerDay,
        IReadOnlyList<string> warnings)
        => new(
            stock.CeruleumTanks,
            stock.ObservedAtUtc?.ToUniversalTime(),
            stock.Source,
            stockUsability,
            reserve,
            tanksPerDay,
            FullFleetSendsRemaining: 0,
            RefillBeforeUtc: null,
            ApproximateRunway: null,
            FuelRunwayStatus.Unavailable,
            warnings.ToArray());

    private static TimeSpan? CalculateApproximateRunway(
        int stock,
        int reserve,
        double tanksPerDay,
        ICollection<string> warnings)
    {
        if (stock <= reserve)
            return TimeSpan.Zero;
        if (tanksPerDay <= 0)
            return null;

        var days = (stock - (double)reserve) / tanksPerDay;
        if (days > TimeSpan.MaxValue.TotalDays)
        {
            warnings.Add("The approximate fuel runway exceeds the supported time range.");
            return null;
        }

        return TimeSpan.FromDays(days);
    }

    private static SimulationResult Simulate(
        int stock,
        int reserve,
        IReadOnlyList<FarmingCyclePlan> cycles,
        DateTimeOffset nowUtc)
    {
        if (cycles.Count == 0)
            return new SimulationResult(null, null, HorizonExceeded: false);

        var events = cycles
            .Select(cycle => new DepartureEvent(
                cycle.NextDepartureAtUtc.ToUniversalTime() < nowUtc
                    ? nowUtc
                    : cycle.NextDepartureAtUtc.ToUniversalTime(),
                cycle.TanksPerVoyage,
                cycle.FullCycleDuration))
            .ToArray();
        var firstDepartureAtUtc = events.Min(item => item.AtUtc);
        var horizonAtUtc = AddClamped(nowUtc, DefensiveSimulationHorizon);
        long remaining = stock;

        for (var groupNumber = 0; groupNumber < MaximumDepartureGroups; groupNumber++)
        {
            var eventAtUtc = events.Min(item => item.AtUtc);
            if (eventAtUtc > horizonAtUtc)
                return new SimulationResult(null, firstDepartureAtUtc, HorizonExceeded: true);

            long debit = 0;
            for (var index = 0; index < events.Length; index++)
            {
                if (events[index].AtUtc != eventAtUtc)
                    continue;
                debit += events[index].Tanks;
            }

            if (remaining - debit < reserve)
                return new SimulationResult(eventAtUtc, firstDepartureAtUtc, HorizonExceeded: false);

            remaining -= debit;
            for (var index = 0; index < events.Length; index++)
            {
                if (events[index].AtUtc == eventAtUtc)
                    events[index] = events[index] with
                    {
                        AtUtc = AddClamped(events[index].AtUtc, events[index].CycleDuration),
                    };
            }
        }

        return new SimulationResult(null, firstDepartureAtUtc, HorizonExceeded: true);
    }

    private static FuelRunwayStatus DetermineStatus(
        DateTimeOffset? refillBeforeUtc,
        DateTimeOffset? firstDepartureAtUtc,
        DateTimeOffset nowUtc)
    {
        if (refillBeforeUtc is null)
            return FuelRunwayStatus.Healthy;
        if (refillBeforeUtc <= nowUtc || refillBeforeUtc == firstDepartureAtUtc)
            return FuelRunwayStatus.Critical;
        return refillBeforeUtc <= AddClamped(nowUtc, LowRunwayThreshold)
            ? FuelRunwayStatus.Low
            : FuelRunwayStatus.Healthy;
    }

    private static DateTimeOffset AddClamped(DateTimeOffset value, TimeSpan duration)
    {
        var utc = value.ToUniversalTime();
        return duration > DateTimeOffset.MaxValue - utc
            ? DateTimeOffset.MaxValue
            : utc + duration;
    }

    private sealed record DepartureEvent(DateTimeOffset AtUtc, int Tanks, TimeSpan CycleDuration);

    private sealed record SimulationResult(
        DateTimeOffset? RefillBeforeUtc,
        DateTimeOffset? FirstDepartureAtUtc,
        bool HorizonExceeded);
}
