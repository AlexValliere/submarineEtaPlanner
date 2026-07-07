using SubmarineEtaPlanner.Planner;

namespace SubmarineEtaPlanner.SubmarineTrackerCompat;

public sealed class CompatSubmarineCatalog : ISubmarineCatalog
{
    private static readonly Dictionary<char, PartStats> PartCatalog = new()
    {
        ['S'] = new PartStats(85, 85, 70, 85, 80),
        ['U'] = new PartStats(115, 95, 75, 115, 95),
        ['W'] = new PartStats(125, 105, 80, 130, 100),
        ['C'] = new PartStats(105, 125, 90, 120, 105),
    };

    private static readonly Dictionary<uint, SectorInfo> Sectors = Enumerable.Range(1, 149)
        .Select(i => CreateSector((uint)i))
        .ToDictionary(s => s.Point, s => s);

    private static readonly uint[] Mrojz = [13, 18, 15, 10, 26];

    public IReadOnlyList<UnlockRule> UnlockRules { get; } =
    [
        new UnlockRule(5, 6, 10),
        new UnlockRule(10, 11, 15),
        new UnlockRule(15, 16, 20, UnlocksSubSlot: true),
        new UnlockRule(20, 21, 25),
        new UnlockRule(26, 27, 30),
        new UnlockRule(30, 32, 35),
        new UnlockRule(35, 36, 40, UnlocksSubSlot: true),
        new UnlockRule(44, 45, 50),
        new UnlockRule(48, 49, 55),
        new UnlockRule(60, 61, 60),
        new UnlockRule(70, 71, 70),
        new UnlockRule(80, 81, 80),
        new UnlockRule(90, 91, 90),
        new UnlockRule(100, 101, 100),
        new UnlockRule(110, 111, 110),
    ];

    public SubmarineBuild ResolveBuild(string buildCode, int rank)
    {
        var normalized = NormalizeBuildCode(buildCode);
        var stats = normalized.Select(c => PartCatalog.TryGetValue(c, out var part) ? part : PartCatalog['S']).ToArray();
        return new SubmarineBuild(
            normalized,
            rank,
            stats.Sum(s => s.Surveillance) + rank,
            stats.Sum(s => s.Retrieval) + rank,
            stats.Sum(s => s.Favor),
            stats.Sum(s => s.Range) + rank * 3,
            Math.Max(40, stats.Sum(s => s.Speed) / stats.Length));
    }

    public SubmarineBuild? ResolveBuild(SubmarineBuildParts buildParts, int rank)
    {
        if (buildParts == SubmarineBuildParts.Empty ||
            buildParts.Hull == 0 ||
            buildParts.Stern == 0 ||
            buildParts.Bow == 0 ||
            buildParts.Bridge == 0)
        {
            return null;
        }

        var buildCode = string.Concat(
            ToIdentifier(buildParts.Hull),
            ToIdentifier(buildParts.Stern),
            ToIdentifier(buildParts.Bow),
            ToIdentifier(buildParts.Bridge));
        return ResolveBuild(buildCode, rank);
    }

    public IReadOnlyList<RouteCandidate> GetCandidateRoutes(
        SubmarineBuild build,
        IReadOnlySet<uint> unlockedPoints,
        IReadOnlySet<uint> exploredPoints,
        IReadOnlySet<uint> mustInclude,
        EtaSettings settings,
        DateTimeOffset? deadlineUtc = null)
    {
        var available = Sectors.Values
            .Where(s => s.RequiredRank <= build.Rank)
            .Where(s => unlockedPoints.Count == 0 || unlockedPoints.Contains(s.Point))
            .OrderByDescending(s => mustInclude.Contains(s.Point))
            .ThenByDescending(s => s.Exp)
            .Take(24)
            .ToArray();

        if (mustInclude.Count > 0)
        {
            var must = Sectors.Values
                .Where(s => mustInclude.Contains(s.Point))
                .Where(s => s.RequiredRank <= build.Rank)
                .ToArray();
            available = must.Concat(available).DistinctBy(s => s.Point).ToArray();
        }

        var candidates = new List<RouteCandidate>();
        foreach (var route in BuildRouteSets(available.Select(s => s.Point).ToArray(), mustInclude))
        {
            var distance = route.Sum(p => Sectors[p].Distance);
            if (distance > build.Range)
                continue;

            var duration = CalculateDuration(route, build);
            var durationLimitHours = settings.EffectiveDurationLimitHours;
            if (durationLimitHours > 0 && duration > TimeSpan.FromHours(durationLimitHours))
                continue;

            var exp = CalculateExp(route, build, settings.EffectiveExpMode);
            var expPerHour = duration.TotalHours <= 0 ? exp : exp / duration.TotalHours;
            var unlockTargets = UnlockRules
                .Where(r => route.Contains(r.SourcePoint))
                .Where(r => r.RequiredRank <= build.Rank)
                .Select(r => r.UnlocksPoint)
                .ToArray();

            candidates.Add(new RouteCandidate(
                route,
                exp,
                duration,
                expPerHour,
                unlockTargets,
                settings.EtaModel,
                durationLimitHours > 0));
        }

        return candidates
            .OrderByDescending(c => mustInclude.Count > 0 && c.Route.Any(mustInclude.Contains))
            .ThenByDescending(c => settings.OptimizeExpPerHour ? c.ExpPerHour : c.Exp)
            .ThenBy(c => c.Duration)
            .ToArray();
    }

    public uint CalculateExp(IReadOnlyList<uint> route, SubmarineBuild build, ExpMode expMode)
    {
        var multiplier = expMode == ExpMode.Average ? 1.2 : 1.0;
        var statBonus = build.Retrieval >= 420 ? 1.25 : build.Retrieval >= 360 ? 1.1 : 1.0;
        return (uint)route
            .Where(Sectors.ContainsKey)
            .Sum(point => Math.Round(Sectors[point].Exp * multiplier * statBonus));
    }

    public TimeSpan CalculateDuration(IReadOnlyList<uint> route, SubmarineBuild build)
    {
        if (route.Count == 0)
            return TimeSpan.Zero;

        var distance = route.Where(Sectors.ContainsKey).Sum(point => Sectors[point].Distance);
        var speed = Math.Max(1, build.Speed);
        var travelHours = Math.Max(1, distance / (double)speed);
        return TimeSpan.FromHours(12 + travelHours);
    }

    public (int Rank, uint CurrentExp, uint NextLevelExp) ApplyExp(int rank, uint currentExp, uint gainedExp, int targetRank)
    {
        var total = currentExp + gainedExp;
        while (rank < targetRank)
        {
            var needed = ExpToNext(rank);
            if (total < needed)
                break;

            total -= needed;
            rank++;
        }

        if (rank >= targetRank)
            total = 0;

        return (rank, total, rank >= targetRank ? 0 : ExpToNext(rank));
    }

    public string PointName(uint point) => Sectors.TryGetValue(point, out var sector) ? $"{sector.MapCode}{sector.Point}" : point.ToString();

    public bool IsPostTargetFarmingReady(SubmarineBuild build, IReadOnlySet<uint> unlockedPoints)
        => build.Code.Equals("WSCC", StringComparison.OrdinalIgnoreCase) && Mrojz.All(unlockedPoints.Contains);

    private static uint ExpToNext(int rank)
        => (uint)(1000 + rank * rank * 24 + rank * 240);

    private static IEnumerable<IReadOnlyList<uint>> BuildRouteSets(uint[] points, IReadOnlySet<uint> mustInclude)
    {
        if (points.Length == 0)
            yield break;

        foreach (var point in points)
            yield return [point];

        for (var size = 2; size <= Math.Min(5, points.Length); size++)
        {
            for (var i = 0; i <= points.Length - size; i++)
            {
                var route = points.Skip(i).Take(size).Distinct().ToArray();
                if (route.Length != size)
                    continue;
                if (mustInclude.Count > 0 && !route.Any(mustInclude.Contains))
                    continue;
                yield return route;
            }
        }
    }

    private static string NormalizeBuildCode(string buildCode)
    {
        var normalized = new string((buildCode ?? string.Empty).ToUpperInvariant().Where(char.IsLetter).Take(4).ToArray());
        if (normalized.Length == 4)
            return normalized;

        return (normalized + "SSSS")[..4];
    }

    private static char ToIdentifier(ushort partId)
        => ((partId - 1) / 4) switch
        {
            0 => 'S',
            1 => 'U',
            2 => 'W',
            3 => 'C',
            _ => 'S',
        };

    private static SectorInfo CreateSector(uint point)
    {
        var mapIndex = (point - 1) / 30;
        var requiredRank = Math.Max(1, (int)point - 2);
        var exp = (uint)(850 + point * 95 + Math.Pow(point, 1.35));
        var distance = 45u + point * 3u + (uint)(mapIndex * 20);
        return new SectorInfo(point, $"M{mapIndex + 1}", requiredRank, exp, distance);
    }

    private sealed record PartStats(int Surveillance, int Retrieval, int Favor, int Range, int Speed);

    private sealed record SectorInfo(uint Point, string MapCode, int RequiredRank, uint Exp, uint Distance);
}
