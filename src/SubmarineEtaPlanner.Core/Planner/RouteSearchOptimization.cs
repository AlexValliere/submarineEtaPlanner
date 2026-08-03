namespace SubmarineEtaPlanner.Planner;

internal sealed class BoundedLruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int maximumEntries;
    private readonly long maximumWeight;
    private readonly Func<TValue, long> getWeight;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> entries = [];
    private readonly LinkedList<Entry> recency = [];
    private long currentWeight;

    public BoundedLruCache(int maximumEntries, long maximumWeight = long.MaxValue, Func<TValue, long>? getWeight = null)
    {
        if (maximumEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        if (maximumWeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumWeight));

        this.maximumEntries = maximumEntries;
        this.maximumWeight = maximumWeight;
        this.getWeight = getWeight ?? (_ => 1);
    }

    public int Count => this.entries.Count;

    public long CurrentWeight => this.currentWeight;

    public bool TryGetValue(TKey key, out TValue value)
    {
        if (!this.entries.TryGetValue(key, out var node))
        {
            value = default!;
            return false;
        }

        this.recency.Remove(node);
        this.recency.AddFirst(node);
        value = node.Value.Value;
        return true;
    }

    public int Set(TKey key, TValue value)
    {
        if (this.entries.TryGetValue(key, out var existing))
        {
            this.currentWeight -= existing.Value.Weight;
            this.recency.Remove(existing);
            this.entries.Remove(key);
        }

        var weight = Math.Max(0, this.getWeight(value));
        if (weight > this.maximumWeight)
            return 0;

        var node = new LinkedListNode<Entry>(new Entry(key, value, weight));
        this.recency.AddFirst(node);
        this.entries[key] = node;
        this.currentWeight += weight;

        var evictions = 0;
        while (this.entries.Count > this.maximumEntries || this.currentWeight > this.maximumWeight)
        {
            var oldest = this.recency.Last!;
            this.recency.RemoveLast();
            this.entries.Remove(oldest.Value.Key);
            this.currentWeight -= oldest.Value.Weight;
            evictions++;
        }

        return evictions;
    }

    private sealed record Entry(TKey Key, TValue Value, long Weight);
}

internal readonly record struct RouteRankValue(int RouteId, double Score, long DurationTicks);

internal static class ExactRouteRanking
{
    internal const double ScoreTieTolerance = 0.001;

    public static int[] Create(IEnumerable<RouteRankValue> values)
        => values
            .OrderByDescending(value => value.Score)
            .ThenBy(value => value.RouteId)
            .Select(value => value.RouteId)
            .ToArray();

    public static int? FindBest(
        IReadOnlyList<int> rankedRouteIds,
        Func<int, bool> isEligible,
        Func<int, RouteRankValue> getValue,
        Func<bool> shouldStop,
        Action checkCancellation,
        out int inspected,
        out bool completed)
    {
        inspected = 0;
        completed = true;
        double? lowestConnectedScore = null;
        var connectedScores = new List<RouteRankValue>();

        foreach (var routeId in rankedRouteIds)
        {
            if ((inspected & 0x3FF) == 0)
            {
                checkCancellation();
                if (shouldStop())
                {
                    completed = false;
                    break;
                }
            }

            inspected++;
            if (!isEligible(routeId))
                continue;

            var value = getValue(routeId);
            // The legacy comparator is path-dependent inside the tolerance. Adjacent scores can
            // therefore form a wider connected group that must be replayed in stable order.
            if (lowestConnectedScore is not null &&
                lowestConnectedScore.Value - value.Score >= ScoreTieTolerance)
            {
                break;
            }

            lowestConnectedScore = value.Score;
            connectedScores.Add(value);
        }

        if (connectedScores.Count == 0)
            return null;

        RouteRankValue? best = null;
        foreach (var candidate in connectedScores.OrderBy(value => value.RouteId))
        {
            if (best is not null && !IsBetter(candidate, best.Value))
                continue;

            best = candidate;
        }

        return best?.RouteId;
    }

    public static bool IsBetter(RouteRankValue candidate, RouteRankValue current)
        => candidate.Score >= current.Score &&
           (Math.Abs(candidate.Score - current.Score) >= ScoreTieTolerance ||
            candidate.DurationTicks < current.DurationTicks);
}
