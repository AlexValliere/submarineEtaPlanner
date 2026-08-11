namespace SubmarineEtaPlanner.Planner;

public enum UnlockDestinationState
{
    Unknown,
    Explored,
    Unlocked,
    Discoverable,
    Locked,
}

public enum UnlockDestinationBlockReason
{
    None,
    InitialDestination,
    SourceLocked,
    EarlierSibling,
    FleetRank,
}

public sealed record UnlockMapActiveAttempt(
    long SubmarineId,
    string SubmarineName,
    DateTimeOffset ReturnAtUtc);

public sealed record UnlockMapDestinationPresentation(
    RouteDestination Destination,
    UnlockDestinationState State,
    UnlockRule? IncomingRule,
    IReadOnlyList<uint> PrerequisitePath,
    IReadOnlyList<UnlockMapActiveAttempt> ActiveAttempts,
    UnlockDestinationBlockReason BlockReason = UnlockDestinationBlockReason.None,
    uint? BlockingPoint = null)
{
    public bool HasActiveAttempt => ActiveAttempts.Count > 0;

    public bool IsRemaining => State is UnlockDestinationState.Discoverable or UnlockDestinationState.Locked;
}

public sealed record UnlockMapConnection(
    uint SourcePoint,
    uint TargetPoint,
    uint SourceMapId,
    uint TargetMapId)
{
    public bool CrossesMaps => SourceMapId != TargetMapId;
}

public sealed record UnlockMapPresentation(
    uint MapId,
    string MapName,
    IReadOnlyList<UnlockMapDestinationPresentation> Destinations,
    IReadOnlyList<UnlockMapConnection> Connections,
    int TotalDestinations,
    int? UnlockedDestinations,
    int? ExploredDestinations,
    int? RemainingDestinations);

public sealed record FcUnlockMapsPresentation(
    string FcIdKey,
    string FcDisplayName,
    bool UnlockDataKnown,
    IReadOnlyList<UnlockMapPresentation> Maps,
    int TotalDestinations,
    int? UnlockedDestinations,
    int? ExploredDestinations,
    int? RemainingDestinations);

public static class UnlockMapPresentationBuilder
{
    public static FcUnlockMapsPresentation Build(
        FcState freeCompany,
        IReadOnlyList<RouteDestination> destinations,
        IReadOnlyList<UnlockRule> unlockRules,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(freeCompany);
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(unlockRules);

        var orderedDestinations = destinations
            .OrderBy(destination => destination.MapId)
            .ThenBy(destination => destination.SectorId)
            .ToArray();
        var destinationsById = orderedDestinations.ToDictionary(destination => destination.SectorId);
        var incomingRules = unlockRules
            .Where(rule => destinationsById.ContainsKey(rule.UnlocksPoint))
            .GroupBy(rule => rule.UnlocksPoint)
            .ToDictionary(group => group.Key, group => group.OrderBy(rule => rule.SourcePoint).First());
        var rulesBySource = unlockRules
            .Where(rule => destinationsById.ContainsKey(rule.UnlocksPoint))
            .GroupBy(rule => rule.SourcePoint)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(rule => rule.UnlocksPoint).ToArray());
        var activeAttempts = BuildActiveAttempts(freeCompany, rulesBySource, now);

        var presentations = orderedDestinations.ToDictionary(
            destination => destination.SectorId,
            destination =>
            {
                incomingRules.TryGetValue(destination.SectorId, out var incomingRule);
                activeAttempts.TryGetValue(destination.SectorId, out var attempts);
                var classification = Classify(freeCompany, destination.SectorId, incomingRule, rulesBySource);
                return new UnlockMapDestinationPresentation(
                    destination,
                    classification.State,
                    incomingRule,
                    BuildPrerequisitePath(destination.SectorId, incomingRules, rulesBySource),
                    attempts ?? [],
                    classification.BlockReason,
                    classification.BlockingPoint);
            });

        var connections = unlockRules
            .Where(rule => destinationsById.ContainsKey(rule.SourcePoint) && destinationsById.ContainsKey(rule.UnlocksPoint))
            .Select(rule => new UnlockMapConnection(
                rule.SourcePoint,
                rule.UnlocksPoint,
                destinationsById[rule.SourcePoint].MapId,
                destinationsById[rule.UnlocksPoint].MapId))
            .Distinct()
            .OrderBy(connection => connection.SourcePoint)
            .ThenBy(connection => connection.TargetPoint)
            .ToArray();

        var maps = orderedDestinations
            .GroupBy(destination => (destination.MapId, destination.MapName))
            .OrderBy(group => group.Key.MapId)
            .Select(group =>
            {
                var mapDestinations = group.Select(destination => presentations[destination.SectorId]).ToArray();
                var mapConnections = connections
                    .Where(connection => connection.SourceMapId == group.Key.MapId || connection.TargetMapId == group.Key.MapId)
                    .ToArray();
                return new UnlockMapPresentation(
                    group.Key.MapId,
                    group.Key.MapName,
                    mapDestinations,
                    mapConnections,
                    mapDestinations.Length,
                    CountKnown(freeCompany, mapDestinations, destination => destination.State is UnlockDestinationState.Explored or UnlockDestinationState.Unlocked),
                    CountKnown(freeCompany, mapDestinations, destination => destination.State == UnlockDestinationState.Explored),
                    CountKnown(freeCompany, mapDestinations, destination => destination.IsRemaining));
            })
            .ToArray();

        return new FcUnlockMapsPresentation(
            freeCompany.FcIdKey,
            freeCompany.DisplayName,
            freeCompany.UnlockDataKnown,
            maps,
            orderedDestinations.Length,
            SumKnown(freeCompany, maps.Select(map => map.UnlockedDestinations)),
            SumKnown(freeCompany, maps.Select(map => map.ExploredDestinations)),
            SumKnown(freeCompany, maps.Select(map => map.RemainingDestinations)));
    }

    private static DestinationClassification Classify(
        FcState freeCompany,
        uint point,
        UnlockRule? incomingRule,
        IReadOnlyDictionary<uint, UnlockRule[]> rulesBySource)
    {
        if (!freeCompany.UnlockDataKnown)
            return new DestinationClassification(UnlockDestinationState.Unknown);
        if (freeCompany.ExploredPoints.Contains(point))
            return new DestinationClassification(UnlockDestinationState.Explored);
        if (freeCompany.UnlockedPoints.Contains(point))
            return new DestinationClassification(UnlockDestinationState.Unlocked);
        if (incomingRule is null)
            return new DestinationClassification(
                UnlockDestinationState.Locked,
                UnlockDestinationBlockReason.InitialDestination);
        if (!freeCompany.UnlockedPoints.Contains(incomingRule.SourcePoint))
            return new DestinationClassification(
                UnlockDestinationState.Locked,
                UnlockDestinationBlockReason.SourceLocked,
                incomingRule.SourcePoint);

        var nextRule = GetNextLockedRule(
            incomingRule.SourcePoint,
            freeCompany.UnlockedPoints,
            rulesBySource,
            int.MaxValue);
        if (nextRule != incomingRule)
            return new DestinationClassification(
                UnlockDestinationState.Locked,
                UnlockDestinationBlockReason.EarlierSibling,
                nextRule?.UnlocksPoint);

        return freeCompany.Submarines.Any(submarine => submarine.Rank >= incomingRule.SourceRequiredRank)
            ? new DestinationClassification(UnlockDestinationState.Discoverable)
            : new DestinationClassification(
                UnlockDestinationState.Locked,
                UnlockDestinationBlockReason.FleetRank);
    }

    private static IReadOnlyDictionary<uint, IReadOnlyList<UnlockMapActiveAttempt>> BuildActiveAttempts(
        FcState freeCompany,
        IReadOnlyDictionary<uint, UnlockRule[]> rulesBySource,
        DateTimeOffset now)
    {
        if (!freeCompany.UnlockDataKnown)
            return new Dictionary<uint, IReadOnlyList<UnlockMapActiveAttempt>>();

        return freeCompany.Submarines
            .Where(submarine => submarine.ReturnAtUtc > now && submarine.CurrentVoyageKnown)
            .SelectMany(submarine =>
            {
                var route = submarine.ManualCurrentRouteOverride.Count > 0
                    ? submarine.ManualCurrentRouteOverride
                    : submarine.CurrentRoute;
                return route
                    .Distinct()
                    .Select(source => GetNextLockedRule(
                        source,
                        freeCompany.UnlockedPoints,
                        rulesBySource,
                        submarine.Rank))
                    .Where(rule => rule is not null)
                    .Select(rule => (Rule: rule!, Attempt: new UnlockMapActiveAttempt(
                        submarine.SubmarineId,
                        submarine.Name,
                        submarine.ReturnAtUtc)));
            })
            .GroupBy(item => item.Rule.UnlocksPoint)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<UnlockMapActiveAttempt>)group
                    .Select(item => item.Attempt)
                    .DistinctBy(attempt => attempt.SubmarineId)
                    .OrderBy(attempt => attempt.ReturnAtUtc)
                    .ThenBy(attempt => attempt.SubmarineName, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
    }

    private static UnlockRule? GetNextLockedRule(
        uint sourcePoint,
        IReadOnlySet<uint> unlockedPoints,
        IReadOnlyDictionary<uint, UnlockRule[]> rulesBySource,
        int rank)
        => rulesBySource.TryGetValue(sourcePoint, out var rules)
            ? rules.FirstOrDefault(rule => rule.SourceRequiredRank <= rank && !unlockedPoints.Contains(rule.UnlocksPoint))
            : null;

    private static IReadOnlyList<uint> BuildPrerequisitePath(
        uint targetPoint,
        IReadOnlyDictionary<uint, UnlockRule> incomingRules,
        IReadOnlyDictionary<uint, UnlockRule[]> rulesBySource)
    {
        var path = new List<uint>();
        var added = new HashSet<uint>();
        var visited = new HashSet<uint>();

        void Visit(uint point)
        {
            if (!visited.Add(point))
                return;
            if (incomingRules.TryGetValue(point, out var rule))
            {
                Visit(rule.SourcePoint);
                if (rulesBySource.TryGetValue(rule.SourcePoint, out var siblings))
                {
                    foreach (var sibling in siblings.Where(sibling => sibling.UnlocksPoint < point))
                        Visit(sibling.UnlocksPoint);
                }
            }
            if (added.Add(point))
                path.Add(point);
        }

        Visit(targetPoint);
        return path;
    }

    private static int? CountKnown(
        FcState freeCompany,
        IEnumerable<UnlockMapDestinationPresentation> destinations,
        Func<UnlockMapDestinationPresentation, bool> predicate)
        => freeCompany.UnlockDataKnown ? destinations.Count(predicate) : null;

    private static int? SumKnown(FcState freeCompany, IEnumerable<int?> values)
        => freeCompany.UnlockDataKnown ? values.Sum(value => value.GetValueOrDefault()) : null;

    private sealed record DestinationClassification(
        UnlockDestinationState State,
        UnlockDestinationBlockReason BlockReason = UnlockDestinationBlockReason.None,
        uint? BlockingPoint = null);
}

public readonly record struct UnlockMapCanvasPoint(float X, float Y);

public static class UnlockMapLayoutCalculator
{
    private const float CoordinateEpsilon = 0.001f;

    public static IReadOnlyDictionary<uint, UnlockMapCanvasPoint> Calculate(
        IReadOnlyList<RouteDestination> destinations,
        float width,
        float height,
        float padding)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        if (destinations.Count == 0)
            return new Dictionary<uint, UnlockMapCanvasPoint>();

        var safeWidth = Math.Max(1f, width);
        var safeHeight = Math.Max(1f, height);
        var safePadding = Math.Clamp(padding, 0f, Math.Min(safeWidth, safeHeight) / 2f);
        var positions = destinations.Select(destination => destination.MapPosition).ToArray();
        if (positions.Any(position => position is null || !float.IsFinite(position.Value.X) || !float.IsFinite(position.Value.Z)))
            return Grid(destinations, safeWidth, safeHeight, safePadding);

        var minX = positions.Min(position => position!.Value.X);
        var maxX = positions.Max(position => position!.Value.X);
        var minZ = positions.Min(position => position!.Value.Z);
        var maxZ = positions.Max(position => position!.Value.Z);
        var rangeX = maxX - minX;
        var rangeZ = maxZ - minZ;
        if (rangeX < CoordinateEpsilon || rangeZ < CoordinateEpsilon)
            return Grid(destinations, safeWidth, safeHeight, safePadding);

        var availableWidth = Math.Max(1f, safeWidth - (safePadding * 2f));
        var availableHeight = Math.Max(1f, safeHeight - (safePadding * 2f));
        var scale = Math.Min(availableWidth / rangeX, availableHeight / rangeZ);
        var contentWidth = rangeX * scale;
        var contentHeight = rangeZ * scale;
        var originX = safePadding + ((availableWidth - contentWidth) / 2f);
        var originY = safePadding + ((availableHeight - contentHeight) / 2f);

        return destinations.ToDictionary(
            destination => destination.SectorId,
            destination => new UnlockMapCanvasPoint(
                originX + ((destination.MapPosition!.Value.X - minX) * scale),
                originY + ((maxZ - destination.MapPosition.Value.Z) * scale)));
    }

    private static IReadOnlyDictionary<uint, UnlockMapCanvasPoint> Grid(
        IReadOnlyList<RouteDestination> destinations,
        float width,
        float height,
        float padding)
    {
        var ordered = destinations.OrderBy(destination => destination.SectorId).ToArray();
        var aspect = Math.Clamp(width / Math.Max(1f, height), 0.5f, 2f);
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(ordered.Length * aspect)));
        var rows = Math.Max(1, (int)Math.Ceiling((double)ordered.Length / columns));
        var availableWidth = Math.Max(1f, width - (padding * 2f));
        var availableHeight = Math.Max(1f, height - (padding * 2f));
        var cellWidth = availableWidth / columns;
        var cellHeight = availableHeight / rows;

        return ordered.Select((destination, index) => new
            {
                destination.SectorId,
                Point = new UnlockMapCanvasPoint(
                    padding + ((index % columns) + 0.5f) * cellWidth,
                    padding + ((index / columns) + 0.5f) * cellHeight),
            })
            .ToDictionary(item => item.SectorId, item => item.Point);
    }
}
