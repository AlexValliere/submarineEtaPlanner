namespace SubmarineEtaPlanner.Planner;

public static class FleetPresentationOrdering
{
    public static IReadOnlyList<FcOperationalProjection> ActionsFirst(
        IEnumerable<FcOperationalProjection> projections,
        Func<FcOperationalProjection, bool> isFavorite)
        => projections
            .OrderByDescending(isFavorite)
            .ThenBy(projection => projection.ActionSortBucket)
            .ThenBy(projection => projection.Submarines
                .Select(submarine => submarine.NextActionAtUtc)
                .Where(value => value is not null)
                .Min() ?? DateTimeOffset.MaxValue)
            .ThenBy(projection => projection.State.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<FcOperationalProjection> FarmReadyEta(
        IEnumerable<FcOperationalProjection> projections,
        Func<FcOperationalProjection, bool> isFavorite)
        => projections
            .OrderByDescending(isFavorite)
            .ThenBy(projection => projection.Mode == FleetMode.Farming ? 0 : 1)
            .ThenBy(projection => projection.Mode == FleetMode.Farming
                ? DateTimeOffset.MinValue
                : projection.CompletionP50AtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(projection => projection.State.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<FcOperationalProjection> ByName(
        IEnumerable<FcOperationalProjection> projections,
        Func<FcOperationalProjection, bool> isFavorite)
        => projections
            .OrderByDescending(isFavorite)
            .ThenBy(projection => projection.State.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
