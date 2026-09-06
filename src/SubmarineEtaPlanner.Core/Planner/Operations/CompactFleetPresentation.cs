namespace SubmarineEtaPlanner.Planner;

internal enum OperationsAttentionFilter { None, Collect, ReturningToday, LowFuel, NeedsSetup }

internal sealed record FleetFuelPresentation(
    ResolvedFuelStock Stock,
    IReadOnlyList<FarmingRoutePlan> Routes,
    IReadOnlyList<FarmingCyclePlan> Cycles,
    FuelRunwayForecast Forecast)
{
    public bool HasFarming => Routes.Count > 0;
    public bool NeedsSetup => HasFarming && (!Stock.IsAvailable || Routes.Any(route => !route.IsUsable));
    public bool LowFuel => HasFarming && Forecast.Status is FuelRunwayStatus.Low or FuelRunwayStatus.Critical;
    public string UnavailableReason => Forecast.Warnings.FirstOrDefault() ?? "Fuel stock is unavailable.";
}

internal sealed record OperationsAttentionSummary(int Collect, int ReturningToday, int LowFuel, int NeedsSetup)
{
    public static OperationsAttentionSummary Create(
        IReadOnlyList<FcOperationalProjection> fleets,
        IReadOnlyDictionary<string, FleetFuelPresentation> fuel,
        DateTimeOffset now,
        TimeZoneInfo zone)
        => new(
            fleets.Sum(fc => fc.Submarines.Count(sub => MatchesSubmarine(sub, fc.State, OperationsAttentionFilter.Collect, now, zone))),
            fleets.Sum(fc => fc.Submarines.Count(sub => MatchesSubmarine(sub, fc.State, OperationsAttentionFilter.ReturningToday, now, zone))),
            fleets.Count(fc => fuel[fc.State.FcIdKey].LowFuel),
            fleets.Count(fc => fuel[fc.State.FcIdKey].NeedsSetup));

    public static bool MatchesFleet(FcOperationalProjection fleet, FleetFuelPresentation fuel,
        OperationsAttentionFilter filter, DateTimeOffset now, TimeZoneInfo zone)
        => filter switch
        {
            OperationsAttentionFilter.None => true,
            OperationsAttentionFilter.LowFuel => fuel.LowFuel,
            OperationsAttentionFilter.NeedsSetup => fuel.NeedsSetup,
            _ => fleet.Submarines.Any(sub => MatchesSubmarine(sub, fleet.State, filter, now, zone)),
        };

    public static bool MatchesSubmarine(SubmarineOperationalProjection sub, FcState fc,
        OperationsAttentionFilter filter, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (filter == OperationsAttentionFilter.Collect)
            return sub.State == OperationalState.ReadyToCollect;
        if (filter != OperationsAttentionFilter.ReturningToday)
            return false;
        var returned = fc.Submarines.FirstOrDefault(item => item.SubmarineId == sub.SubmarineId)?.ReturnAtUtc;
        return returned is { } timestamp && timestamp > now &&
               TimeZoneInfo.ConvertTime(timestamp, zone).Date == TimeZoneInfo.ConvertTime(now, zone).Date;
    }
}

internal sealed record CompactSubmarinePresentation(string Status, RecommendedAction Action,
    IReadOnlyList<uint> CurrentRoute, IReadOnlyList<uint> NextRoute, string NextRouteLabel, string? Reason)
{
    public static CompactSubmarinePresentation Create(SubmarineOperationalProjection sub, SubmarineState tracked,
        FarmingRoutePlan? farming)
    {
        var status = sub.State switch
        {
            OperationalState.ReadyToCollect => "To collect",
            OperationalState.Underway => "Underway",
            OperationalState.Syncing => "Syncing",
            _ => "Idle",
        };
        var current = tracked.ReturnAtUtc != DateTimeOffset.MinValue && tracked.CurrentVoyageKnown
            ? tracked.CurrentRoute : [];
        var next = sub.RecommendedNextRoute;
        var label = (sub.AlternativeRoutes.Count > 1 || sub.AlternativeRoutes.Any(outcome => outcome.RequiredProjectedUnlocks.Count > 0)) ? "Conditional next" : "Proposed next";
        var action = sub.Action;
        var reason = sub.ProjectionUnavailableReason;
        if (sub.EffectiveRole == EffectiveSubmarineRole.Paused)
            return new(status + " · Paused", RecommendedAction.Paused, current, [], label, null);
        if (sub.EffectiveRole == EffectiveSubmarineRole.Farming)
        {
            next = farming?.Route ?? [];
            label = farming?.Source == FarmingRouteSource.Pinned ? "Pinned next" : "Tracked next";
            reason = farming?.IsUsable == true ? null : string.Join(" ", farming?.Warnings ?? ["Choose a farming route."]);
            action = sub.State == OperationalState.Syncing ? RecommendedAction.WaitForTracker
                : farming?.IsUsable != true ? RecommendedAction.ChooseFarmingRoute
                : sub.State switch
                {
                    OperationalState.ReadyToCollect => RecommendedAction.CollectAndResendFarmingRouteNow,
                    OperationalState.Idle => RecommendedAction.SendFarmingRouteNow,
                    _ => RecommendedAction.ResendFarmingRouteAfterCollection,
                };
        }
        else if (sub.State == OperationalState.Syncing || next.Count == 0)
        {
            action = sub.State == OperationalState.ReadyToCollect
                ? RecommendedAction.CollectThenWaitForTracker : RecommendedAction.WaitForTracker;
        }
        return new(status, action, current, next, label, reason);
    }

    public string ActionLabel => Action switch
    {
        RecommendedAction.WaitForTracker => NextRoute.Count == 0 && Status != "Syncing" ? "Wait for forecast / data" : "Wait for tracker",
        RecommendedAction.ChooseFarmingRoute => "Review farming setup",
        RecommendedAction.CollectThenWaitForTracker => "Collect; wait for tracker",
        RecommendedAction.SendLevelingRouteNow => "Send proposed route",
        RecommendedAction.SendLevelingRouteAfterCollection => "Collect, then review route",
        RecommendedAction.CollectAndResendFarmingRouteNow => "Collect and resend",
        RecommendedAction.SendFarmingRouteNow => "Send farming route",
        RecommendedAction.ResendFarmingRouteAfterCollection => "Resend after collection",
        RecommendedAction.Paused => "Paused",
        _ => "No action",
    };
}

/// <summary>Input changes and time boundaries invalidate presentation work independently of ImGui frames.</summary>
internal sealed class PresentationCache<T>
{
    private readonly Dictionary<string, Entry> entries = new();
    private sealed record Entry(string Fingerprint, DateTimeOffset Created, DateTimeOffset Expires, T Value);

    public T Get(string id, string fingerprint, DateTimeOffset now, Func<(T Value, DateTimeOffset? Boundary)> create)
    {
        if (entries.TryGetValue(id, out var entry) && entry.Fingerprint == fingerprint && now >= entry.Created && now < entry.Expires)
            return entry.Value;
        var result = create();
        var expires = now.AddMinutes(1);
        if (result.Boundary is { } boundary && boundary > now && boundary < expires)
            expires = boundary;
        entries[id] = new(fingerprint, now, expires, result.Value);
        return result.Value;
    }

    public void Retain(IReadOnlySet<string> ids)
    {
        foreach (var id in entries.Keys.Where(id => !ids.Contains(id)).ToArray()) entries.Remove(id);
    }
}

internal sealed record FcNavigationRequest<T>(string FcId, T Destination, bool FocusFuel = false);
internal enum DraftNavigationChoice { Save, Discard, Cancel }

internal sealed class FcNavigationGuard<T>
{
    public FcNavigationRequest<T>? Pending { get; private set; }

    public bool Request(FcNavigationRequest<T> request, string? currentFc, bool dirty, bool replacesDraft)
    {
        if (replacesDraft && dirty && !string.Equals(currentFc, request.FcId, StringComparison.OrdinalIgnoreCase))
        {
            Pending = request;
            return false;
        }
        return true;
    }

    public FcNavigationRequest<T>? Resolve(DraftNavigationChoice choice, Action save)
    {
        var request = Pending;
        if (request is not null && choice == DraftNavigationChoice.Save) save();
        Pending = null;
        return choice == DraftNavigationChoice.Cancel ? null : request;
    }
}
