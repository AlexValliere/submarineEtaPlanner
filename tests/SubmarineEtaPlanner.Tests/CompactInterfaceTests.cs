using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class CompactInterfaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AttentionCountsUseDisplayedFleetsAndPreservePausedVoyages()
    {
        var paused = Sub(1, OperationalState.ReadyToCollect, EffectiveSubmarineRole.Paused);
        var syncing = Sub(2, OperationalState.Syncing);
        var future = Sub(3, OperationalState.Underway);
        var fleet = Fleet([paused, syncing, future], [Now.AddHours(-1), Now.AddHours(-2), Now.AddHours(2)]);
        var fuel = Fuel(FuelRunwayStatus.Low);
        var summary = OperationsAttentionSummary.Create([fleet], new Dictionary<string, FleetFuelPresentation> { [fleet.State.FcIdKey] = fuel }, Now, TimeZoneInfo.Utc);
        Assert.Equal(new OperationsAttentionSummary(1, 1, 1, 0), summary);
        Assert.True(OperationsAttentionSummary.MatchesFleet(fleet, fuel, OperationsAttentionFilter.Collect, Now, TimeZoneInfo.Utc));
        Assert.Equal(3, fleet.Submarines.Count);
        Assert.False(OperationsAttentionSummary.MatchesSubmarine(syncing, fleet.State, OperationsAttentionFilter.Collect, Now, TimeZoneInfo.Utc));
    }

    [Fact]
    public void ReturningTodayUsesLocalDateAndActualReturnRatherThanNextAction()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test +2", TimeSpan.FromHours(2), "+2", "+2");
        var now = new DateTimeOffset(2026, 9, 6, 21, 0, 0, TimeSpan.Zero);
        var sub = Sub(1, OperationalState.Syncing) with { NextActionAtUtc = now };
        bool Matches(DateTimeOffset returned) => OperationsAttentionSummary.MatchesSubmarine(
            sub, Fleet([sub], [returned]).State, OperationsAttentionFilter.ReturningToday, now, zone);
        Assert.True(Matches(now.AddMinutes(59)));
        Assert.False(Matches(now.AddHours(1)));
        Assert.False(Matches(now));
        Assert.False(Matches(DateTimeOffset.MinValue));
    }

    [Theory]
    [InlineData(3, 29, 22)] // The spring clock change makes this local day 23 hours long.
    [InlineData(10, 25, 23)] // The autumn clock change makes this local day 25 hours long.
    public void ReturningTodayHandlesDaylightSaving(int month, int day, int endUtcHour)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
        var now = new DateTimeOffset(2026, month, day, 0, 0, 0, TimeSpan.Zero);
        var boundary = new DateTimeOffset(2026, month, day, endUtcHour, 0, 0, TimeSpan.Zero);
        var sub = Sub(1, OperationalState.Underway);
        Assert.True(OperationsAttentionSummary.MatchesSubmarine(sub, Fleet([sub], [boundary.AddSeconds(-1)]).State,
            OperationsAttentionFilter.ReturningToday, now, zone));
        Assert.False(OperationsAttentionSummary.MatchesSubmarine(sub, Fleet([sub], [boundary]).State,
            OperationsAttentionFilter.ReturningToday, now, zone));
    }

    [Fact]
    public void MixedRoleFiltersPrecedeAttentionCounts()
    {
        var mixed = Fleet([Sub(1, OperationalState.ReadyToCollect)], [Now]) with { RoleSummary = new(1, 1, 0) };
        var leveling = Fleet([Sub(2, OperationalState.ReadyToCollect)], [Now], 2) with { RoleSummary = new(1, 0, 0) };
        var displayed = new[] { mixed, leveling }.Where(fc => FleetPresentationFiltering.Includes(fc, FleetMode.Farming)).ToArray();
        var fuel = displayed.ToDictionary(fc => fc.State.FcIdKey, _ => Fuel(FuelRunwayStatus.Healthy));
        Assert.Equal(1, OperationsAttentionSummary.Create(displayed, fuel, Now, TimeZoneInfo.Utc).Collect);
    }

    [Fact]
    public void MissingFuelAndStaleFuelRemainDifferentFromLowStock()
    {
        var missing = Fuel(FuelRunwayStatus.Unavailable) with { Stock = Stock() with { CeruleumTanks = null, UnavailableReason = "Choose fuel holder" } };
        Assert.True(missing.NeedsSetup);
        Assert.False(missing.LowFuel);
        var stale = Fuel(FuelRunwayStatus.Unavailable) with { Forecast = Forecast(FuelRunwayStatus.Unavailable) with { StockUsability = FuelStockUsability.StaleAfterKnownDeparture } };
        Assert.False(stale.NeedsSetup);
        Assert.False(stale.LowFuel);
        Assert.True(Fuel(FuelRunwayStatus.Critical).LowFuel);
        Assert.False((missing with { Routes = [] }).NeedsSetup);
        Assert.False((Fuel(FuelRunwayStatus.Critical) with { Routes = [] }).LowFuel);
    }

    [Fact]
    public void UnusableFarmingRouteNeedsSetupEvenWithKnownStock()
    {
        var fuel = Fuel(FuelRunwayStatus.Unavailable) with { Routes = [Plan() with { VoyageDuration = null }] };
        Assert.True(fuel.NeedsSetup);
    }

    [Fact]
    public void PinnedFarmingRouteIsOnlyTheNextRouteAndDoesNotReplaceCurrentVoyage()
    {
        var sub = Sub(1, OperationalState.Underway);
        var tracked = Tracked(1, Now.AddHours(2)) with { CurrentRoute = [1, 2] };
        var pinned = Plan() with { Source = FarmingRouteSource.Pinned, Route = [4, 3] };
        var row = CompactSubmarinePresentation.Create(sub, tracked, pinned);
        Assert.Equal([1u, 2u], row.CurrentRoute);
        Assert.Equal([4u, 3u], row.NextRoute);
        Assert.Equal("Pinned next", row.NextRouteLabel);
        Assert.Equal(RecommendedAction.ResendFarmingRouteAfterCollection, row.Action);
        var idle = CompactSubmarinePresentation.Create(sub with { State = OperationalState.Idle }, tracked with { ReturnAtUtc = DateTimeOffset.MinValue }, pinned);
        Assert.Empty(idle.CurrentRoute);
        Assert.Equal(RecommendedAction.SendFarmingRouteNow, idle.Action);
    }

    [Fact]
    public void UnknownRouteAndPausedAssignmentsNeverRecommendSending()
    {
        var tracked = Tracked(1, Now) with { CurrentVoyageKnown = false };
        var sync = CompactSubmarinePresentation.Create(Sub(1, OperationalState.Syncing), tracked, Plan());
        Assert.Empty(sync.CurrentRoute);
        Assert.Equal(RecommendedAction.WaitForTracker, sync.Action);
        var paused = CompactSubmarinePresentation.Create(Sub(1, OperationalState.Idle, EffectiveSubmarineRole.Paused), tracked, Plan());
        Assert.Equal(RecommendedAction.Paused, paused.Action);
        Assert.Empty(paused.NextRoute);
        var broken = CompactSubmarinePresentation.Create(Sub(1, OperationalState.Idle), tracked, Plan() with { VoyageDuration = null });
        Assert.Equal(RecommendedAction.ChooseFarmingRoute, broken.Action);
    }

    [Fact]
    public void ConditionalLevelingRouteRetainsItsQualification()
    {
        var sub = Sub(1, OperationalState.Underway, EffectiveSubmarineRole.Leveling) with { AlternativeRoutes = [new([2, 3], .5, [3]), new([1, 2], .5, [])] };
        // Existing simulation outcomes are not flattened into a guaranteed route.
        var row = CompactSubmarinePresentation.Create(sub, Tracked(1, Now.AddHours(1)), null);
        Assert.Equal("Conditional next", row.NextRouteLabel);
        Assert.Equal(sub.RecommendedNextRoute, row.NextRoute);
    }

    [Fact]
    public void CacheExpiresOnBoundaryMinuteInputChangeAndClockRollback()
    {
        var cache = new PresentationCache<int>();
        var calls = 0;
        int Get(DateTimeOffset now, string key = "a") => cache.Get("fc", key, now, () => (++calls, now.AddSeconds(30)));
        Assert.Equal(1, Get(Now));
        Assert.Equal(1, Get(Now.AddSeconds(29)));
        Assert.Equal(2, Get(Now.AddSeconds(30)));
        Assert.Equal(3, Get(Now.AddSeconds(31), "b"));
        Assert.Equal(4, Get(Now.AddSeconds(1), "b"));
        var minuteCache = new PresentationCache<int>();
        Assert.Equal(5, minuteCache.Get("fc", "a", Now, () => (++calls, null)));
        Assert.Equal(5, minuteCache.Get("fc", "a", Now.AddSeconds(59), () => (++calls, null)));
        Assert.Equal(6, minuteCache.Get("fc", "a", Now.AddMinutes(1), () => (++calls, null)));
    }

    [Fact]
    public void FuelFingerprintTracksRelevantInputsButIgnoresFavorite()
    {
        var fc = Fleet([Sub(1, OperationalState.Idle)], [DateTimeOffset.MinValue]).State;
        var prefs = new FcPreferences { Submarines = new() { [1] = new() { Assignment = SubmarineAssignment.Farming, PinnedFarmingRoute = [1, 2] } } };
        string Key(ResolvedFuelStock? stock = null, int delay = 120, int target = 90, FcState? state = null)
            => FuelPresentationFingerprint.Create(state ?? fc, prefs, target, delay, stock ?? Stock());
        var initial = Key();
        prefs.Favorite = true;
        Assert.Equal(initial, Key());
        Assert.NotEqual(initial, Key(Stock() with { CeruleumTanks = 500 }));
        Assert.NotEqual(initial, Key(Stock() with { ObservedAtUtc = Now.AddMinutes(1) }));
        Assert.NotEqual(initial, Key(delay: 10));
        Assert.NotEqual(initial, Key(target: 100));
        Assert.NotEqual(initial, Key(state: fc with { Submarines = [fc.Submarines[0] with { ReturnAtUtc = Now }] }));
        prefs.Submarines[1].PinnedFarmingRoute = [2, 1];
        Assert.NotEqual(initial, Key());
        var reordered = Key();
        prefs.Submarines[1].CollectionDelayMinutes = 10;
        Assert.NotEqual(reordered, Key());
        var delayed = Key();
        prefs.Submarines[1].Assignment = SubmarineAssignment.Paused;
        Assert.NotEqual(delayed, Key());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DraftNavigationRetainsDestinationAndSavesOnlyOnSave(int choiceValue)
    {
        var choice = (DraftNavigationChoice)choiceValue;
        var guard = new FcNavigationGuard<string>();
        var target = new FcNavigationRequest<string>("b", "setup", true);
        Assert.False(guard.Request(target, "a", dirty: true, replacesDraft: true));
        var saves = 0;
        var destination = guard.Resolve(choice, () => saves++);
        Assert.Equal(choice == DraftNavigationChoice.Save ? 1 : 0, saves);
        Assert.Equal(choice == DraftNavigationChoice.Cancel ? null : target, destination);
        Assert.Null(guard.Pending);
    }

    [Fact]
    public void OrdinaryNavigationAndSameFcShortcutsDoNotReplaceDrafts()
    {
        var guard = new FcNavigationGuard<string>();
        Assert.True(guard.Request(new("b", "income"), "a", true, false));
        Assert.True(guard.Request(new("A", "setup"), "a", true, true));
        Assert.Null(guard.Pending);
    }

    [Fact]
    public void FavoriteSurvivesDiscardingOrSavingUnrelatedDraft()
    {
        var preferences = new FcPreferences { TargetRankOverride = 90 };
        var staged = FcSetupDraft.Capture(preferences, []) with { TargetRankOverride = 100 };
        preferences.Favorite = true;
        var discarded = FcSetupDraft.Capture(preferences, []);
        Assert.Equal(90, discarded.TargetRankOverride);
        Assert.True(preferences.Favorite);
        staged.ApplyTo(preferences);
        Assert.True(preferences.Favorite);
        Assert.Equal(100, preferences.TargetRankOverride);
    }

    private static SubmarineOperationalProjection Sub(long id, OperationalState state,
        EffectiveSubmarineRole role = EffectiveSubmarineRole.Farming)
        => new(id, $"Sub {id}", 90, 90, state, state.ToString(), RecommendedAction.None, false,
            Now, [1, 2], [2, 3], RoutePurpose.Farming, 100, 90, null, 0, null, []) { EffectiveRole = role };

    private static SubmarineState Tracked(long id, DateTimeOffset returned)
        => new([1], id, $"Sub {id}", 90, 0, 100, SubmarineBuildParts.Empty, returned, [1, 2], true, []);

    private static FcOperationalProjection Fleet(SubmarineOperationalProjection[] subs, DateTimeOffset[] returns, byte id = 1)
        => new(new FcState([id], "TEST", "World", new HashSet<uint>(), new HashSet<uint>(),
            subs.Select((sub, i) => Tracked(sub.SubmarineId, returns[i])).ToArray()), null, 90, FleetMode.Farming, subs, null, null, null);

    private static ResolvedFuelStock Stock() => new(1000, FuelStockSourceKind.Manual, null, null, null, null, false, null);
    private static FarmingRoutePlan Plan() => new(1, "Sub 1", FarmingRouteSource.CurrentTrackerRoute, [1, 2],
        new("SSSS", null), new(10, true, []), TimeSpan.FromDays(1), []);
    private static FuelRunwayForecast Forecast(FuelRunwayStatus status) => new(1000, null, FuelStockSourceKind.Manual,
        FuelStockUsability.Current, 10, 10, 40, 99, Now.AddDays(20), TimeSpan.FromDays(20), status, []);
    private static FleetFuelPresentation Fuel(FuelRunwayStatus status) => new(Stock(), [Plan()], [], Forecast(status));
}
