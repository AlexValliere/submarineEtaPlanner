namespace SubmarineEtaPlanner.Planner;

public sealed record FarmingCyclePlan(
    long SubmarineId,
    string SubmarineName,
    IReadOnlyList<uint> Route,
    int TanksPerVoyage,
    TimeSpan VoyageDuration,
    TimeSpan CollectionDelay,
    TimeSpan FullCycleDuration,
    DateTimeOffset NextDepartureAtUtc,
    DateTimeOffset? CurrentVoyageDepartureAtUtc,
    bool CurrentVoyageAlreadyPaid,
    FarmingRouteSource RouteSource);

public static class FarmingCyclePlanBuilder
{
    public static IReadOnlyList<FarmingCyclePlan> Build(
        FcState freeCompany,
        IReadOnlyList<FarmingRoutePlan> routePlans,
        FcPreferences preferences,
        EtaSettings settings,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(freeCompany);
        ArgumentNullException.ThrowIfNull(routePlans);
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(settings);

        var submarines = freeCompany.Submarines.ToDictionary(submarine => submarine.SubmarineId);
        var plans = new List<FarmingCyclePlan>(routePlans.Count);
        foreach (var routePlan in routePlans)
        {
            if (!routePlan.IsUsable ||
                routePlan.VoyageDuration is not { } voyageDuration ||
                routePlan.Fuel.CeruleumTanks is not { } tanksPerVoyage ||
                !submarines.TryGetValue(routePlan.SubmarineId, out var submarine))
            {
                continue;
            }

            var collectionDelayMinutes = preferences.Submarines
                .GetValueOrDefault(routePlan.SubmarineId)?
                .CollectionDelayMinutes ?? settings.CollectionDelayMinutes;
            if (collectionDelayMinutes < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(settings),
                    collectionDelayMinutes,
                    "Collection delay cannot be negative.");
            }

            var collectionDelay = TimeSpan.FromMinutes(collectionDelayMinutes);
            var currentVoyageAlreadyPaid = submarine.ReturnAtUtc != DateTimeOffset.MinValue;
            var currentVoyageDepartureAtUtc = currentVoyageAlreadyPaid && submarine.CurrentVoyageKnown
                ? submarine.ReturnAtUtc - voyageDuration
                : (DateTimeOffset?)null;
            var nextDepartureAtUtc = currentVoyageAlreadyPaid && submarine.ReturnAtUtc > now
                ? submarine.ReturnAtUtc + collectionDelay
                : now;

            plans.Add(new FarmingCyclePlan(
                routePlan.SubmarineId,
                routePlan.SubmarineName,
                routePlan.Route.ToArray(),
                tanksPerVoyage,
                voyageDuration,
                collectionDelay,
                voyageDuration + collectionDelay,
                nextDepartureAtUtc.ToUniversalTime(),
                currentVoyageDepartureAtUtc?.ToUniversalTime(),
                currentVoyageAlreadyPaid,
                routePlan.Source));
        }

        return plans.ToArray();
    }
}
