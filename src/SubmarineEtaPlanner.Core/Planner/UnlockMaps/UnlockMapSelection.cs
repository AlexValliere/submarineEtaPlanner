namespace SubmarineEtaPlanner.Planner;

internal static class UnlockMapSelection
{
    public static IReadOnlyList<UnlockMapDestinationPresentation> Search(FcUnlockMapsPresentation presentation, string query)
        => presentation.Maps.SelectMany(map => map.Destinations)
            .Where(item => item.Destination.Code.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) ||
                           item.Destination.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Destination.MapId).ThenBy(item => item.Destination.SectorId).ToArray();

    public static HashSet<uint> Path(FcUnlockMapsPresentation presentation, uint? selected)
    {
        var all = presentation.Maps.SelectMany(map => map.Destinations).ToDictionary(item => item.Destination.SectorId);
        var path = new HashSet<uint>();
        if (selected is not { } point || !all.TryGetValue(point, out var destination)) return path;
        path.Add(point);
        path.UnionWith(destination.RemainingUnlockPath);
        var visited = new HashSet<uint> { point };
        // Follow incoming rules as well: already-unlocked prerequisites provide useful context.
        while (all.TryGetValue(point, out var current) && current.IncomingRule is { } rule)
        {
            point = rule.SourcePoint;
            path.Add(point);
            // Invalid/cyclic catalog data must not hang the UI.
            if (!visited.Add(point)) break;
        }
        return path;
    }

    public static HashSet<uint> Visible(FcUnlockMapsPresentation presentation, bool remainingOnly, uint? selected)
    {
        var all = presentation.Maps.SelectMany(map => map.Destinations).ToArray();
        if (!remainingOnly || !presentation.UnlockDataKnown)
            return all.Select(item => item.Destination.SectorId).ToHashSet();
        var visible = new HashSet<uint>();
        foreach (var destination in all.Where(item => item.IsRemaining))
            visible.UnionWith(Path(presentation, destination.Destination.SectorId));
        visible.UnionWith(Path(presentation, selected));
        return visible;
    }
}
