using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class FleetReworkTests
{
    [Theory]
    [InlineData(FcStrategyPreset.Recommended, EtaModel.PracticalLeveling, RouteGoal.UnlockLevelingRoutesThenLevel, true)]
    [InlineData(FcStrategyPreset.ImmediateExpOnly, EtaModel.ExactRouteSearch, RouteGoal.FastestLevelingOnly, false)]
    [InlineData(FcStrategyPreset.SlotsFirstThenImmediateExp, EtaModel.ExactRouteSearch, RouteGoal.UnlockSubSlotsThenLevel, true)]
    [InlineData(FcStrategyPreset.UnlockEverythingThenLevel, EtaModel.ExactRouteSearch, RouteGoal.UnlockEverythingThenLevel, true)]
    public void EffectiveSettingsApplyEveryStrategyPreset(
        FcStrategyPreset preset,
        EtaModel expectedModel,
        RouteGoal expectedGoal,
        bool expectedSlotPriority)
    {
        var global = EtaSettings.CreateDefault() with { TargetRank = 90 };

        var effective = EffectiveEtaSettingsResolver.Resolve(
            global,
            new FcSimulationOverride(999, preset),
            maximumRank: 120);

        Assert.Equal(120, effective.TargetRank);
        Assert.Equal(expectedModel, effective.EtaModel);
        Assert.Equal(expectedGoal, effective.RouteGoal);
        Assert.Equal(expectedSlotPriority, effective.PrioritizeSubSlots);
        Assert.NotSame(global, effective);
    }

    [Fact]
    public void EffectiveSettingsInheritWithoutMutatingGlobal()
    {
        var global = EtaSettings.CreateDefault() with { TargetRank = 85 };
        var effective = EffectiveEtaSettingsResolver.Resolve(global, null, 120);

        effective.TargetRank = 100;

        Assert.Equal(85, global.TargetRank);
        Assert.Equal(global.RouteGoal, effective.RouteGoal);
    }

    [Theory]
    [InlineData(50, true, true, "Collect now; send the modeled route after synchronization")]
    [InlineData(50, false, false, "Send recommended leveling route now")]
    [InlineData(100, true, true, "Collect and resend farming route now")]
    [InlineData(100, false, true, "Resend farming route after collection")]
    public void ActionProjectionUsesRankAndVoyageState(
        int rank,
        bool returned,
        bool hasRoute,
        string expectedAction)
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        var route = hasRoute ? new uint[] { 1 } : [];
        var returnAt = returned ? now.AddMinutes(-1) : hasRoute ? now.AddHours(2) : DateTimeOffset.MinValue;
        var fc = CreateFc(rank, returnAt, route, currentKnown: true);
        var projection = FleetPresentationBuilder.Create(fc, CreateResult(fc, 90, now),
            EtaSettings.CreateDefault() with { TargetRank = 90 }, new StubCatalog(), now);

        Assert.Equal(expectedAction, Assert.Single(projection.Submarines).ActionLabel);
        Assert.Equal(rank >= 90 ? FleetMode.Farming : FleetMode.Leveling, projection.Mode);
    }

    [Fact]
    public void SyncingVoyageExplainsTrackerDependency()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        var fc = CreateFc(50, now.AddHours(2), [], currentKnown: false);

        var submarine = Assert.Single(FleetPresentationBuilder.Create(
            fc,
            CreateResult(fc, 90, now),
            EtaSettings.CreateDefault() with { TargetRank = 90 },
            new StubCatalog(),
            now).Submarines);

        Assert.Equal(OperationalState.Syncing, submarine.State);
        Assert.Contains("SubmarineTracker", submarine.ActionLabel);
    }

    [Fact]
    public void TargetReadySubmarineWithoutKnownRouteRequiresAChoice()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        var fc = CreateFc(90, DateTimeOffset.MinValue, [], currentKnown: true);

        var submarine = Assert.Single(FleetPresentationBuilder.Create(
            fc,
            CreateResult(fc, 90, now),
            EtaSettings.CreateDefault() with { TargetRank = 90 },
            new StubCatalog(),
            now).Submarines);

        Assert.Equal("Choose farming route", submarine.ActionLabel);
        Assert.Empty(submarine.DisplayedRoute);
    }

    [Fact]
    public void FarmingVoyageProjectsPastTheFcTargetToCatalogMaximum()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        var fc = CreateFc(90, now.AddHours(2), [1], currentKnown: true);

        var submarine = Assert.Single(FleetPresentationBuilder.Create(
            fc,
            CreateResult(fc, 90, now),
            EtaSettings.CreateDefault() with { TargetRank = 90 },
            new StubCatalog(),
            now).Submarines);

        Assert.Equal((uint)1_000, submarine.ExpectedExp);
        Assert.Equal(100, submarine.ProjectedRank);
    }

    [Fact]
    public void IncomeIncludesZeroGilVoyagesInDenominatorAndHonorsWindow()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(100);
        var fc = CreateFc(90, DateTimeOffset.MinValue, [], currentKnown: true);
        var submarine = fc.Submarines[0] with
        {
            Salvage = new SubmarineSalvageSummary(3, now.AddDays(-40), now.AddDays(-1), [])
            {
                Voyages =
                [
                    new(fc.FcIdKey, 1, now.AddDays(-40), [new SalvageItemTotal(1, "Old", 100, 10)]),
                    new(fc.FcIdKey, 1, now.AddDays(-2), [new SalvageItemTotal(1, "Salvage", 100, 10)]),
                    new(fc.FcIdKey, 1, now.AddDays(-1), []),
                ],
            },
        };
        fc = fc with { Submarines = [submarine] };

        var metrics = IncomeMetricsCalculator.Calculate(fc, now, TimeSpan.FromDays(30));

        Assert.Equal(1_000, metrics.GrossGil);
        Assert.Equal(2, metrics.ValidVoyages);
        Assert.Equal(500, metrics.GilPerVoyage);
        Assert.Equal(now.AddDays(-2), metrics.FirstReturnAtUtc);
    }

    [Fact]
    public void OperationsOrderingKeepsFavoritesFirstThenActionsAndReturns()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(10);
        var catalog = new StubCatalog();
        var settings = EtaSettings.CreateDefault() with { TargetRank = 90 };
        FcOperationalProjection Projection(string tag, DateTimeOffset returnAt, IReadOnlyList<uint> route)
        {
            var fc = CreateFc(50, returnAt, route, currentKnown: true) with { FreeCompanyTag = tag };
            return FleetPresentationBuilder.Create(fc, CreateResult(fc, 90, now), settings, catalog, now);
        }
        var futureLate = Projection("Future late", now.AddHours(5), [1]);
        var immediate = Projection("Immediate", DateTimeOffset.MinValue, []);
        var futureEarly = Projection("Future early", now.AddHours(1), [1]);
        var favoriteLate = Projection("Favorite", now.AddHours(10), [1]);

        var ordered = FleetPresentationOrdering.ActionsFirst(
            [futureLate, immediate, favoriteLate, futureEarly],
            projection => projection.State.FreeCompanyTag == "Favorite");

        Assert.Equal(["Favorite", "Immediate", "Future early", "Future late"],
            ordered.Select(projection => projection.State.FreeCompanyTag).ToArray());
    }

    private static FcState CreateFc(int rank, DateTimeOffset returnAt, IReadOnlyList<uint> route, bool currentKnown)
    {
        byte[] id = [1];
        return new FcState(
            id,
            "TEST",
            "World",
            new HashSet<uint> { 1 },
            new HashSet<uint> { 1 },
            [new SubmarineState(id, 1, "Sub", rank, 0, 100, new SubmarineBuildParts(1, 1, 1, 1), returnAt, route, currentKnown, [])]);
    }

    private static EtaResult CreateResult(FcState fc, int target, DateTimeOffset now)
    {
        var submarine = fc.Submarines[0];
        var plan = new VoyagePlan(
            submarine.SubmarineId, submarine.Name, now, now.AddHours(1), "TEST", [1], 1_000,
            submarine.Rank, Math.Min(100, submarine.Rank + 10), 0, 0, [], [], TimeSpan.FromHours(1), 1_000,
            EtaModel.PracticalLeveling, false);
        var perSub = new PerSubEtaResult(
            submarine.SubmarineId, submarine.Name, submarine.Rank, Math.Max(target, submarine.Rank), now.AddHours(1),
            TimeSpan.FromHours(1), 1, "TEST", [1], [plan], [], [], CalculationStatus.Complete, null)
        {
            NextRouteOutcomes = [new RouteOutcome([1], 1, [])],
        };
        return new EtaResult(fc.FcId, fc.DisplayName, now, target, SimulationMode.Fleet, [perSub], now.AddHours(1), 1, [plan], [], [], CalculationStatus.Complete, null);
    }

    private sealed class StubCatalog : ISubmarineCatalog
    {
        public int MaximumRank => 100;
        public IReadOnlyList<UnlockRule> UnlockRules => [];
        public SubmarineBuild ResolveBuild(string buildCode, int rank) => new(buildCode, rank, 0, 0, 0, 999, 100);
        public SubmarineBuild? ResolveBuild(SubmarineBuildParts buildParts, int rank) => buildParts == SubmarineBuildParts.Empty ? null : ResolveBuild("TEST", rank);
        public RouteSearchResult FindBestRoute(RouteSearchRequest request) => new(null, 0, false);
        public uint CalculateExp(IReadOnlyList<uint> route, SubmarineBuild build, ExpMode expMode) => 1_000;
        public TimeSpan CalculateDuration(IReadOnlyList<uint> route, SubmarineBuild build) => TimeSpan.FromHours(1);
        public (int Rank, uint CurrentExp, uint NextLevelExp) ApplyExp(int rank, uint currentExp, uint gainedExp, int targetRank)
            => (Math.Min(targetRank, rank + 10), 0, 100);
        public string PointName(uint point) => point.ToString();
        public int GetPointRequiredRank(uint point) => 1;
    }
}
