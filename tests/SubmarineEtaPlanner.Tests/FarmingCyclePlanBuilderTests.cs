using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class FarmingCyclePlanBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UnderwaySubmarineDepartsAfterReturnAndCollectionDelay()
    {
        var returnAt = Now.AddHours(7);
        var plan = Assert.Single(Build(
            [CreateSubmarine(1, returnAt)],
            [CreateRoutePlan(1, TimeSpan.FromHours(5))],
            globalCollectionDelayMinutes: 90));

        Assert.Equal(returnAt.AddMinutes(90), plan.NextDepartureAtUtc);
        Assert.Equal(returnAt.AddHours(-5), plan.CurrentVoyageDepartureAtUtc);
        Assert.True(plan.CurrentVoyageAlreadyPaid);
        Assert.Equal(TimeSpan.FromHours(6.5), plan.FullCycleDuration);
        Assert.Equal("Sub 1", plan.SubmarineName);
        Assert.Equal([1u], plan.Route);
        Assert.Equal(FarmingRouteSource.Pinned, plan.RouteSource);
    }

    [Fact]
    public void ReadyToCollectSubmarineCanDepartNow()
    {
        var returnAt = Now.AddMinutes(-20);
        var plan = Assert.Single(Build(
            [CreateSubmarine(1, returnAt)],
            [CreateRoutePlan(1, TimeSpan.FromHours(5))],
            globalCollectionDelayMinutes: 90));

        Assert.Equal(Now, plan.NextDepartureAtUtc);
        Assert.Equal(returnAt.AddHours(-5), plan.CurrentVoyageDepartureAtUtc);
        Assert.True(plan.CurrentVoyageAlreadyPaid);
    }

    [Fact]
    public void IdleSubmarineCanDepartNowAndHasNoPaidVoyage()
    {
        var plan = Assert.Single(Build(
            [CreateSubmarine(1, DateTimeOffset.MinValue)],
            [CreateRoutePlan(1, TimeSpan.FromHours(5))],
            globalCollectionDelayMinutes: 90));

        Assert.Equal(Now, plan.NextDepartureAtUtc);
        Assert.Null(plan.CurrentVoyageDepartureAtUtc);
        Assert.False(plan.CurrentVoyageAlreadyPaid);
    }

    [Fact]
    public void PerSubmarineCollectionDelayOverridesGlobalDelay()
    {
        var returnAt = Now.AddHours(7);
        var plan = Assert.Single(Build(
            [CreateSubmarine(1, returnAt)],
            [CreateRoutePlan(1, TimeSpan.FromHours(5))],
            globalCollectionDelayMinutes: 120,
            submarineDelays: new Dictionary<long, int?> { [1] = 15 }));

        Assert.Equal(TimeSpan.FromMinutes(15), plan.CollectionDelay);
        Assert.Equal(returnAt.AddMinutes(15), plan.NextDepartureAtUtc);
    }

    [Fact]
    public void GlobalCollectionDelayIsFallbackWithoutOverride()
    {
        var plan = Assert.Single(Build(
            [CreateSubmarine(1, Now.AddHours(7))],
            [CreateRoutePlan(1, TimeSpan.FromHours(5))],
            globalCollectionDelayMinutes: 75,
            submarineDelays: new Dictionary<long, int?> { [1] = null }));

        Assert.Equal(TimeSpan.FromMinutes(75), plan.CollectionDelay);
        Assert.Equal(TimeSpan.FromMinutes(375), plan.FullCycleDuration);
    }

    [Fact]
    public void ZeroCollectionDelayAddsNothingToCycle()
    {
        var duration = TimeSpan.FromHours(5);
        var returnAt = Now.AddHours(7);
        var plan = Assert.Single(Build(
            [CreateSubmarine(1, returnAt)],
            [CreateRoutePlan(1, duration)],
            globalCollectionDelayMinutes: 120,
            submarineDelays: new Dictionary<long, int?> { [1] = 0 }));

        Assert.Equal(TimeSpan.Zero, plan.CollectionDelay);
        Assert.Equal(duration, plan.FullCycleDuration);
        Assert.Equal(returnAt, plan.NextDepartureAtUtc);
    }

    [Fact]
    public void CurrentVoyageDepartureIsDerivedFromReturnAndRouteDuration()
    {
        var returnAt = Now.AddHours(10);
        var duration = TimeSpan.FromHours(8);
        var plan = Assert.Single(Build(
            [CreateSubmarine(1, returnAt)],
            [CreateRoutePlan(1, duration)]));

        Assert.Equal(Now.AddHours(2), plan.CurrentVoyageDepartureAtUtc);
    }

    [Fact]
    public void MissingDurationDoesNotProduceAZeroLengthCycle()
    {
        var plans = Build(
            [CreateSubmarine(1, Now.AddHours(10))],
            [CreateRoutePlan(1, voyageDuration: null)]);

        Assert.Empty(plans);
    }

    [Fact]
    public void FourSubmarinesKeepTheirOwnRecurringCycles()
    {
        var submarines = new[]
        {
            CreateSubmarine(1, Now.AddHours(10)),
            CreateSubmarine(2, Now.AddHours(12)),
            CreateSubmarine(3, Now.AddHours(-1)),
            CreateSubmarine(4, DateTimeOffset.MinValue),
        };
        var routePlans = new[]
        {
            CreateRoutePlan(1, TimeSpan.FromHours(4), tanks: 5),
            CreateRoutePlan(2, TimeSpan.FromHours(6), tanks: 8),
            CreateRoutePlan(3, TimeSpan.FromHours(8), tanks: 11),
            CreateRoutePlan(4, TimeSpan.FromHours(10), tanks: 14),
        };

        var plans = Build(
            submarines,
            routePlans,
            globalCollectionDelayMinutes: 30,
            submarineDelays: new Dictionary<long, int?> { [1] = 5 });

        Assert.Equal(4, plans.Count);
        Assert.Equal(TimeSpan.FromMinutes(245), plans[0].FullCycleDuration);
        Assert.Equal(TimeSpan.FromMinutes(390), plans[1].FullCycleDuration);
        Assert.Equal(TimeSpan.FromMinutes(510), plans[2].FullCycleDuration);
        Assert.Equal(TimeSpan.FromMinutes(630), plans[3].FullCycleDuration);
        Assert.Equal([5, 8, 11, 14], plans.Select(plan => plan.TanksPerVoyage));
        Assert.Equal(
            [Now.AddHours(10).AddMinutes(5), Now.AddHours(12.5), Now, Now],
            plans.Select(plan => plan.NextDepartureAtUtc));
    }

    [Fact]
    public void CurrentVoyageIsMarkedPaidInsteadOfBecomingAFutureDebit()
    {
        var plan = Assert.Single(Build(
            [CreateSubmarine(1, Now.AddHours(7))],
            [CreateRoutePlan(1, TimeSpan.FromHours(5), tanks: 17)]));

        Assert.Equal(17, plan.TanksPerVoyage);
        Assert.True(plan.CurrentVoyageAlreadyPaid);
        Assert.Equal(Now.AddHours(9), plan.NextDepartureAtUtc);
    }

    [Fact]
    public void UnknownCurrentVoyageDoesNotInventADepartureFromPinnedRouteDuration()
    {
        var submarine = CreateSubmarine(1, Now.AddHours(7)) with { CurrentVoyageKnown = false };

        var plan = Assert.Single(Build(
            [submarine],
            [CreateRoutePlan(1, TimeSpan.FromHours(5), tanks: 17)]));

        Assert.True(plan.CurrentVoyageAlreadyPaid);
        Assert.Null(plan.CurrentVoyageDepartureAtUtc);
    }

    private static IReadOnlyList<FarmingCyclePlan> Build(
        IReadOnlyList<SubmarineState> submarines,
        IReadOnlyList<FarmingRoutePlan> routePlans,
        int globalCollectionDelayMinutes = 120,
        IReadOnlyDictionary<long, int?>? submarineDelays = null)
    {
        var preferences = new FcPreferences
        {
            Submarines = submarineDelays?.ToDictionary(
                pair => pair.Key,
                pair => new SubmarinePreferences { CollectionDelayMinutes = pair.Value }) ?? [],
        };
        return FarmingCyclePlanBuilder.Build(
            new FcState([1], "FC", "World", new HashSet<uint>(), new HashSet<uint>(), submarines),
            routePlans,
            preferences,
            new EtaSettings { CollectionDelayMinutes = globalCollectionDelayMinutes },
            Now);
    }

    private static FarmingRoutePlan CreateRoutePlan(
        long submarineId,
        TimeSpan? voyageDuration,
        int tanks = 10)
        => new(
            submarineId,
            $"Sub {submarineId}",
            FarmingRouteSource.Pinned,
            [(uint)submarineId],
            new CurrentBuildPresentation("SSUW", null),
            new RouteFuelProfile(tanks, IsComplete: true, UnknownSectors: []),
            voyageDuration,
            voyageDuration is null ? ["Voyage duration is zero or unavailable."] : []);

    private static SubmarineState CreateSubmarine(long submarineId, DateTimeOffset returnAtUtc)
        => new(
            [1],
            submarineId,
            $"Sub {submarineId}",
            90,
            0,
            1_000,
            new SubmarineBuildParts(1, 2, 3, 4),
            returnAtUtc,
            returnAtUtc == DateTimeOffset.MinValue ? [] : [(uint)submarineId],
            CurrentVoyageKnown: returnAtUtc != DateTimeOffset.MinValue,
            ManualCurrentRouteOverride: []);
}
