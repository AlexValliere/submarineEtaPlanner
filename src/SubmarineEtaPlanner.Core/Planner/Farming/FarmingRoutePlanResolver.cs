namespace SubmarineEtaPlanner.Planner;

public enum FarmingRouteSource
{
    Pinned,
    CurrentTrackerRoute,
    Missing,
}

public sealed record FarmingRoutePlan(
    long SubmarineId,
    string SubmarineName,
    FarmingRouteSource Source,
    IReadOnlyList<uint> Route,
    CurrentBuildPresentation Build,
    RouteFuelProfile Fuel,
    TimeSpan? VoyageDuration,
    IReadOnlyList<string> Warnings)
{
    public bool IsUsable =>
        Route.Count > 0 &&
        Build.IsAvailable &&
        Fuel.IsComplete &&
        VoyageDuration.HasValue &&
        VoyageDuration.Value > TimeSpan.Zero;
}

public static class FarmingRoutePlanResolver
{
    public static IReadOnlyList<FarmingRoutePlan> Resolve(
        FcState freeCompany,
        FcPreferences preferences,
        int effectiveTargetRank,
        ISubmarineCatalog submarineCatalog,
        IRouteOperationalCatalog operationalCatalog)
    {
        ArgumentNullException.ThrowIfNull(freeCompany);
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(submarineCatalog);
        ArgumentNullException.ThrowIfNull(operationalCatalog);

        return freeCompany.Submarines
            .Select(submarine => (
                Submarine: submarine,
                Preferences: preferences.Submarines.GetValueOrDefault(submarine.SubmarineId)))
            .Where(item => SubmarineRoleResolver.Resolve(
                item.Preferences?.Assignment ?? SubmarineAssignment.Auto,
                item.Submarine.Rank,
                effectiveTargetRank) == EffectiveSubmarineRole.Farming)
            .Select(item => ResolveSubmarine(
                item.Submarine,
                item.Preferences?.PinnedFarmingRoute,
                submarineCatalog,
                operationalCatalog))
            .ToArray();
    }

    private static FarmingRoutePlan ResolveSubmarine(
        SubmarineState submarine,
        IReadOnlyList<uint>? pinnedRoute,
        ISubmarineCatalog submarineCatalog,
        IRouteOperationalCatalog operationalCatalog)
    {
        var (source, route) = SelectRoute(submarine, pinnedRoute);
        var build = submarineCatalog.ResolveBuild(submarine.BuildParts, submarine.Rank);
        var buildPresentation = CurrentBuildPresentation.Create(build);

        RouteFuelProfile fuel;
        TimeSpan? duration;
        if (route.Count == 0)
        {
            fuel = new RouteFuelProfile(null, IsComplete: false, UnknownSectors: []);
            duration = null;
        }
        else if (build is null)
        {
            fuel = SnapshotFuel(operationalCatalog.CalculateFuel(route));
            duration = null;
        }
        else
        {
            var operationalProfile = operationalCatalog.AnalyzeOrderedRoute(route, build);
            fuel = SnapshotFuel(operationalProfile.Fuel);
            duration = operationalProfile.Duration > TimeSpan.Zero
                ? operationalProfile.Duration
                : null;
        }

        var warnings = BuildWarnings(route, buildPresentation, fuel, duration);
        return new FarmingRoutePlan(
            submarine.SubmarineId,
            submarine.Name,
            source,
            route,
            buildPresentation,
            fuel,
            duration,
            warnings);
    }

    private static (FarmingRouteSource Source, IReadOnlyList<uint> Route) SelectRoute(
        SubmarineState submarine,
        IReadOnlyList<uint>? pinnedRoute)
    {
        if (pinnedRoute is { Count: > 0 })
            return (FarmingRouteSource.Pinned, pinnedRoute.ToArray());

        if (submarine.CurrentVoyageKnown && submarine.CurrentRoute.Count > 0)
            return (FarmingRouteSource.CurrentTrackerRoute, submarine.CurrentRoute.ToArray());

        return (FarmingRouteSource.Missing, []);
    }

    private static RouteFuelProfile SnapshotFuel(RouteFuelProfile fuel)
        => new(
            fuel.CeruleumTanks,
            fuel.IsComplete,
            fuel.UnknownSectors.Distinct().Order().ToArray());

    private static IReadOnlyList<string> BuildWarnings(
        IReadOnlyList<uint> route,
        CurrentBuildPresentation build,
        RouteFuelProfile fuel,
        TimeSpan? duration)
    {
        var warnings = new List<string>(5);
        if (route.Count == 0)
            warnings.Add("Farming route is empty.");
        if (!build.IsAvailable)
            warnings.Add("Current build could not be resolved.");
        if (fuel.UnknownSectors.Count > 0)
            warnings.Add($"Route contains unknown sectors: {string.Join(", ", fuel.UnknownSectors)}.");
        if (duration is null || duration <= TimeSpan.Zero)
            warnings.Add("Voyage duration is zero or unavailable.");
        if (!fuel.IsComplete)
            warnings.Add("Fuel calculation is incomplete.");
        return warnings.ToArray();
    }
}
