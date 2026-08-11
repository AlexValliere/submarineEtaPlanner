using System.Numerics;

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
    IReadOnlyList<uint> RemainingUnlockPath,
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
                    BuildRemainingUnlockPath(
                        freeCompany,
                        destination.SectorId,
                        classification.State,
                        incomingRules,
                        rulesBySource),
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

    private static IReadOnlyList<uint> BuildRemainingUnlockPath(
        FcState freeCompany,
        uint targetPoint,
        UnlockDestinationState state,
        IReadOnlyDictionary<uint, UnlockRule> incomingRules,
        IReadOnlyDictionary<uint, UnlockRule[]> rulesBySource)
    {
        if (!freeCompany.UnlockDataKnown || state is not (UnlockDestinationState.Discoverable or UnlockDestinationState.Locked))
            return [];

        var fullPath = BuildStructuralPath(targetPoint, incomingRules, rulesBySource);
        for (var index = fullPath.Count - 1; index >= 0; index--)
        {
            var point = fullPath[index];
            if (freeCompany.UnlockedPoints.Contains(point) || freeCompany.ExploredPoints.Contains(point))
                return fullPath.Skip(index).ToArray();
        }

        return fullPath;
    }

    private static IReadOnlyList<uint> BuildStructuralPath(
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
    private const int RelaxationIterations = 220;
    private const int CollisionCleanupIterations = 80;

    public static IReadOnlyDictionary<uint, UnlockMapCanvasPoint> Calculate(
        IReadOnlyList<RouteDestination> destinations,
        IReadOnlyList<UnlockMapConnection> connections,
        float width,
        float height,
        float padding,
        float nodeRadius)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(connections);
        if (destinations.Count == 0)
            return new Dictionary<uint, UnlockMapCanvasPoint>();

        var ordered = destinations.OrderBy(destination => destination.SectorId).ToArray();
        var safeWidth = Math.Max(1f, width);
        var safeHeight = Math.Max(1f, height);
        var safePadding = Math.Clamp(padding, 0f, Math.Min(safeWidth, safeHeight) / 2f);
        var safeRadius = Math.Max(0f, nodeRadius);
        var center = new Vector2(safeWidth / 2f, safeHeight / 2f);
        var left = Math.Min(center.X, safePadding + safeRadius);
        var right = Math.Max(center.X, safeWidth - safePadding - safeRadius);
        var top = Math.Min(center.Y, safePadding + safeRadius);
        var bottom = Math.Max(center.Y, safeHeight - safePadding - safeRadius);
        var anchors = InitialPositions(ordered, left, right, top, bottom);
        var positions = anchors.ToArray();
        var indexBySector = ordered
            .Select((destination, index) => (destination.SectorId, index))
            .ToDictionary(item => item.SectorId, item => item.index);
        var edges = connections
            .Where(connection => !connection.CrossesMaps)
            .Where(connection => indexBySector.ContainsKey(connection.SourcePoint) && indexBySector.ContainsKey(connection.TargetPoint))
            .Select(connection => (
                Source: indexBySector[connection.SourcePoint],
                Target: indexBySector[connection.TargetPoint],
                connection.SourcePoint,
                connection.TargetPoint))
            .Distinct()
            .OrderBy(edge => edge.SourcePoint)
            .ThenBy(edge => edge.TargetPoint)
            .ToArray();

        var minimumSpacing = Math.Max(1f, (safeRadius * 2f) + 12f);
        var preferredEdgeLength = Math.Max(minimumSpacing * 1.25f, safeRadius * 5.5f);
        var maximumUsefulEdgeLength = Math.Max(minimumSpacing * 1.25f, Math.Min(right - left, bottom - top) * 0.45f);
        preferredEdgeLength = Math.Min(preferredEdgeLength, maximumUsefulEdgeLength);
        var interactionDistance = preferredEdgeLength * 1.35f;
        var forces = new Vector2[positions.Length];

        for (var iteration = 0; iteration < RelaxationIterations; iteration++)
        {
            Array.Clear(forces);
            for (var first = 0; first < positions.Length; first++)
            {
                for (var second = first + 1; second < positions.Length; second++)
                {
                    var delta = positions[second] - positions[first];
                    var distance = delta.Length();
                    var direction = distance < CoordinateEpsilon
                        ? DeterministicDirection(ordered[first].SectorId, ordered[second].SectorId)
                        : delta / distance;
                    if (distance >= interactionDistance)
                        continue;

                    var proximity = (interactionDistance - distance) / interactionDistance;
                    var magnitude = proximity * proximity * minimumSpacing * 0.12f;
                    if (distance < minimumSpacing)
                        magnitude += (minimumSpacing - distance) * 0.75f;
                    var force = direction * magnitude;
                    forces[first] -= force;
                    forces[second] += force;
                }
            }

            foreach (var edge in edges)
            {
                var delta = positions[edge.Target] - positions[edge.Source];
                var distance = delta.Length();
                var direction = distance < CoordinateEpsilon
                    ? DeterministicDirection(edge.SourcePoint, edge.TargetPoint)
                    : delta / distance;
                var force = direction * ((distance - preferredEdgeLength) * 0.035f);
                forces[edge.Source] += force;
                forces[edge.Target] -= force;
            }

            var progress = iteration / (float)(RelaxationIterations - 1);
            var maximumStep = minimumSpacing * (0.32f - (progress * 0.28f));
            for (var index = 0; index < positions.Length; index++)
            {
                forces[index] += (anchors[index] - positions[index]) * 0.006f;
                var forceLength = forces[index].Length();
                var step = forceLength > maximumStep
                    ? forces[index] / forceLength * maximumStep
                    : forces[index];
                positions[index] = Clamp(positions[index] + step, left, right, top, bottom);
            }
        }

        for (var iteration = 0; iteration < CollisionCleanupIterations; iteration++)
        {
            var collisionFound = false;
            for (var first = 0; first < positions.Length; first++)
            {
                for (var second = first + 1; second < positions.Length; second++)
                {
                    var delta = positions[second] - positions[first];
                    var distance = delta.Length();
                    if (distance >= minimumSpacing - CoordinateEpsilon)
                        continue;

                    collisionFound = true;
                    var direction = distance < CoordinateEpsilon
                        ? DeterministicDirection(ordered[first].SectorId, ordered[second].SectorId)
                        : delta / distance;
                    var displacement = direction * (((minimumSpacing - distance) / 2f) + 0.01f);
                    positions[first] = Clamp(positions[first] - displacement, left, right, top, bottom);
                    positions[second] = Clamp(positions[second] + displacement, left, right, top, bottom);
                }
            }
            if (!collisionFound)
                break;
        }

        return ordered.Select((destination, index) => new
            {
                destination.SectorId,
                Point = new UnlockMapCanvasPoint(positions[index].X, positions[index].Y),
            })
            .ToDictionary(item => item.SectorId, item => item.Point);
    }

    private static Vector2[] InitialPositions(
        IReadOnlyList<RouteDestination> destinations,
        float left,
        float right,
        float top,
        float bottom)
    {
        var sourcePositions = destinations.Select(destination => destination.MapPosition).ToArray();
        if (sourcePositions.Any(position => position is null || !float.IsFinite(position.Value.X) || !float.IsFinite(position.Value.Z)))
            return Grid(destinations.Count, left, right, top, bottom);

        var minimumX = sourcePositions.Min(position => position!.Value.X);
        var maximumX = sourcePositions.Max(position => position!.Value.X);
        var minimumZ = sourcePositions.Min(position => position!.Value.Z);
        var maximumZ = sourcePositions.Max(position => position!.Value.Z);
        var rangeX = maximumX - minimumX;
        var rangeZ = maximumZ - minimumZ;
        if (rangeX < CoordinateEpsilon || rangeZ < CoordinateEpsilon)
            return Grid(destinations.Count, left, right, top, bottom);

        return sourcePositions.Select(position => new Vector2(
                left + (((position!.Value.X - minimumX) / rangeX) * (right - left)),
                top + (((maximumZ - position.Value.Z) / rangeZ) * (bottom - top))))
            .ToArray();
    }

    private static Vector2[] Grid(int count, float left, float right, float top, float bottom)
    {
        var width = Math.Max(1f, right - left);
        var height = Math.Max(1f, bottom - top);
        var aspect = Math.Clamp(width / height, 0.5f, 2f);
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count * aspect)));
        var rows = Math.Max(1, (int)Math.Ceiling((double)count / columns));
        var cellWidth = (right - left) / columns;
        var cellHeight = (bottom - top) / rows;
        return Enumerable.Range(0, count)
            .Select(index => new Vector2(
                left + ((index % columns) + 0.5f) * cellWidth,
                top + ((index / columns) + 0.5f) * cellHeight))
            .ToArray();
    }

    private static Vector2 DeterministicDirection(uint firstSectorId, uint secondSectorId)
    {
        var angleIndex = (firstSectorId * 31u + secondSectorId * 17u) % 360u;
        var angle = angleIndex * (MathF.PI / 180f);
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    private static Vector2 Clamp(Vector2 point, float left, float right, float top, float bottom)
        => new(Math.Clamp(point.X, left, right), Math.Clamp(point.Y, top, bottom));
}
