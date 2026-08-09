namespace SubmarineEtaPlanner.Planner;

internal sealed class RouteOperationalCalculator : IRouteOperationalCatalog
{
    private readonly IReadOnlyDictionary<uint, int> tankRequirementBySector;
    private readonly Func<IReadOnlyList<uint>, SubmarineBuild, TimeSpan> calculateDuration;

    public RouteOperationalCalculator(
        IReadOnlyDictionary<uint, int> tankRequirementBySector,
        Func<IReadOnlyList<uint>, SubmarineBuild, TimeSpan> calculateDuration)
    {
        this.tankRequirementBySector = tankRequirementBySector;
        this.calculateDuration = calculateDuration;
    }

    public RouteFuelProfile CalculateFuel(IReadOnlyCollection<uint> sectors)
    {
        var ceruleumTanks = 0;
        var visitedSectors = new HashSet<uint>();
        var unknownSectors = new List<uint>();

        foreach (var sector in sectors)
        {
            if (!visitedSectors.Add(sector))
                continue;

            if (!this.tankRequirementBySector.TryGetValue(sector, out var tankRequirement))
            {
                unknownSectors.Add(sector);
                continue;
            }

            ceruleumTanks = checked(ceruleumTanks + tankRequirement);
        }

        return new RouteFuelProfile(
            ceruleumTanks,
            unknownSectors.Count == 0,
            unknownSectors.ToArray());
    }

    public OrderedRouteOperationalProfile AnalyzeOrderedRoute(
        IReadOnlyList<uint> route,
        SubmarineBuild build)
    {
        var orderedRoute = route.ToArray();
        var fuel = CalculateFuel(orderedRoute);
        var duration = orderedRoute.Length == 0
            ? TimeSpan.Zero
            : this.calculateDuration(orderedRoute, build);

        return new OrderedRouteOperationalProfile(orderedRoute, fuel, duration);
    }
}
