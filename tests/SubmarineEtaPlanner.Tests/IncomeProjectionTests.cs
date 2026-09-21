using SubmarineEtaPlanner.Planner;
using Xunit;
using static SubmarineEtaPlanner.Tests.IncomeProjectionTestData;

namespace SubmarineEtaPlanner.Tests;

public sealed class IncomeProjectionTests
{
    [Theory]
    [InlineData(30, 6_000_000)]
    [InlineData(90, 18_000_000)]
    [InlineData(365, 73_000_000)]
    public void FourFarmersProduceExpectedSteadyIncome(int days, long expected)
    {
        var result = Calculate([Fc(1, Enumerable.Range(1, 4).Select(id => Sub(id, 10)).ToArray())]);
        var fc = Assert.Single(result.FreeCompanies);
        Assert.Equal(200_000m, fc.Totals.GilPerDay);
        Assert.Equal((decimal)expected, fc.Totals.ProjectedGil((IncomeProjectionHorizon)days));
        Assert.Equal(new IncomeProjectionTotals(200_000m, 4, 4), IncomeProjectionPresentation.Summarize(result.FreeCompanies));
        Assert.All(fc.Submarines, sub =>
        {
            Assert.Equal(100_000m, sub.AverageGilPerVoyage);
            Assert.Equal(TimeSpan.FromHours(48), sub.FullCycleDuration);
            Assert.Equal(IncomeProjectionSampleSource.Own, sub.Sample.Source);
            Assert.Equal(10, sub.Sample.ReturnCount);
            Assert.Equal(1, sub.Sample.ContributingFcCount);
        });
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(9, false)]
    [InlineData(10, true)]
    public void MinimumSampleSeparatesUnavailableFromSupportedZero(int count, bool available)
    {
        var fc = Assert.Single(Calculate([Fc(1, Sub(1, count, gil: 0))]).FreeCompanies);
        var sub = Assert.Single(fc.Submarines);
        Assert.Equal(available, sub.IsAvailable);
        Assert.Equal(count, sub.Sample.ReturnCount);
        Assert.Equal(available ? 0m : (decimal?)null, fc.Totals.GilPerDay);
        Assert.Equal(available ? null : "Insufficient matching history", sub.UnavailableReason);
    }

    [Fact]
    public void ZeroReturnsAreInDenominatorWithoutInventingMissingReturns()
    {
        var sub = Sub(1, 9) with { VoyageHistory = Sub(1, 9).VoyageHistory.Append(Voyage(1, Now, 0)).ToArray() };
        var estimate = Assert.Single(Assert.Single(Calculate([Fc(1, sub)]).FreeCompanies).Submarines);
        Assert.Equal(10, estimate.Sample.ReturnCount);
        Assert.Equal(90_000m, estimate.AverageGilPerVoyage);
        Assert.Equal(45_000m, estimate.GilPerDay);
    }

    [Fact]
    public void OwnSufficientSampleTakesPrecedenceOverPooledHistory()
    {
        var result = Calculate([Fc(1, Sub(1, 10, 100_000)), Fc(2, Sub(1, 20, 200_000))]);
        Assert.Equal(100_000m, result.FreeCompanies[0].Submarines[0].AverageGilPerVoyage);
        Assert.Equal(200_000m, result.FreeCompanies[1].Submarines[0].AverageGilPerVoyage);
        Assert.All(result.FreeCompanies, fc => Assert.Equal(IncomeProjectionSampleSource.Own, fc.Submarines[0].Sample.Source));
    }

    [Fact]
    public void PoolIncludesOwnReturnsOnceAndKeepsFcAndSubmarineIdentitiesSeparate()
    {
        var first = Sub(1, 5, 100_000);
        // Repeated canonical observations must not be added twice to either sample.
        first = first with { VoyageHistory = first.VoyageHistory.Concat(first.VoyageHistory).ToArray() };
        var result = Calculate([Fc(1, first), Fc(2, Sub(1, 5, 200_000))]);
        Assert.All(result.FreeCompanies, fc =>
        {
            var sub = Assert.Single(fc.Submarines);
            Assert.Equal(150_000m, sub.AverageGilPerVoyage);
            Assert.Equal(new IncomeProjectionSample(IncomeProjectionSampleSource.Pooled, 10, 2, Now.AddDays(-5), Now.AddDays(-1),
                MatchedStats: new(100, 110, 120)), sub.Sample);
        });
    }

    [Fact]
    public void HiddenFcsAreExcludedButHistoryFromPausedAndLevelingDonorsIsUsable()
    {
        var fcs = new[] { Fc(1, Sub(1, 0)), Fc(2, Sub(1, 5)), Fc(3, Sub(1, 5)), Fc(4, Sub(1, 50, 9_999_999)) };
        var preferences = new Dictionary<string, FcPreferences>
        {
            ["02"] = Prefs((1, SubmarineAssignment.Paused)),
            ["03"] = Prefs((1, SubmarineAssignment.Leveling)),
            ["04"] = new() { Hidden = true },
        };
        var result = Calculate(fcs, preferences);
        Assert.Equal(3, result.FreeCompanies.Count);
        var estimate = result.FreeCompanies[0].Submarines[0];
        Assert.Equal(100_000m, estimate.AverageGilPerVoyage);
        Assert.Equal(10, estimate.Sample.ReturnCount);
        Assert.Equal(2, estimate.Sample.ContributingFcCount);
        Assert.Empty(result.FreeCompanies[1].Submarines);
        Assert.Empty(result.FreeCompanies[2].Submarines);
    }

    [Fact]
    public void ExactStatsAndSectorSetAreRequiredButRankAndHistoricalSectorOrderAreNotKeys()
    {
        var matching = Sub(1, 10).VoyageHistory.Select(voyage => voyage with { SectorIds = [2, 1, 2], Rank = 60 });
        var wrong = Sub(2, 10, 5_000_000).VoyageHistory;
        var sub = Sub(1, 0) with
        {
            VoyageHistory = matching
                .Concat(wrong.Select(v => v with { ReturnAtUtc = v.ReturnAtUtc.AddHours(1), Surveillance = 101 }))
                .Concat(wrong.Select(v => v with { ReturnAtUtc = v.ReturnAtUtc.AddHours(2), Retrieval = 111 }))
                .Concat(wrong.Select(v => v with { ReturnAtUtc = v.ReturnAtUtc.AddHours(3), Favor = 121 }))
                .Concat(wrong.Select(v => v with { ReturnAtUtc = v.ReturnAtUtc.AddHours(4), SectorIds = [1] })).ToArray(),
        };
        var estimate = Calculate([Fc(1, sub)]).FreeCompanies[0].Submarines[0];
        Assert.Equal(10, estimate.Sample.ReturnCount);
        Assert.Equal(100_000m, estimate.AverageGilPerVoyage);
    }

    [Fact]
    public void WindowIsInclusiveAndExcludesOlderAndFutureObservations()
    {
        var boundary = Now.AddDays(-90);
        var sub = Sub(1, 8) with
        {
            VoyageHistory = Sub(1, 8).VoyageHistory.Concat(new[]
            {
                Voyage(1, boundary, 100_000), Voyage(1, Now, 100_000),
                Voyage(1, boundary.AddTicks(-1), 9_999_999), Voyage(1, Now.AddTicks(1), 9_999_999),
            }).ToArray(),
        };
        var result = Calculate([Fc(1, sub)]);
        var estimate = result.FreeCompanies[0].Submarines[0];
        Assert.Equal(10, estimate.Sample.ReturnCount);
        Assert.Equal(100_000m, estimate.AverageGilPerVoyage);
        Assert.Equal(boundary, estimate.Sample.FirstReturnAtUtc);
        Assert.Equal(Now, estimate.Sample.LastReturnAtUtc);
        Assert.Equal(Now.AddTicks(1), result.NextRefreshAtUtc);
    }

    [Fact]
    public void RolesRespectManualAssignmentsAndPerFcTargets()
    {
        var prefs = Prefs((1, SubmarineAssignment.Paused), (2, SubmarineAssignment.Leveling), (3, SubmarineAssignment.Farming));
        prefs.TargetRankOverride = 110;
        var result = Calculate([Fc(1, Sub(1, 10), Sub(2, 10), Sub(3, 10) with { Rank = 30 },
            Sub(4, 10), Sub(5, 10) with { Rank = 110 })], new Dictionary<string, FcPreferences> { ["01"] = prefs });
        Assert.Equal([3L, 5L], result.FreeCompanies[0].Submarines.Select(sub => sub.SubmarineId));
    }

    [Fact]
    public void PinnedOrderedRouteAndDelayOverrideDriveCycleWhileHistoryUsesSectorSets()
    {
        var preferences = new Dictionary<string, FcPreferences>
        {
            ["01"] = new() { Submarines = { [1] = new() { PinnedFarmingRoute = [2, 1], CollectionDelayMinutes = 240 } } },
        };
        var catalog = new ProjectionCatalog { Duration = route => route.SequenceEqual(new uint[] { 2, 1 }) ? TimeSpan.FromHours(20) : TimeSpan.FromHours(46) };
        var sub = Calculate([Fc(1, Sub(1, 10))], preferences, catalog).FreeCompanies[0].Submarines[0];
        Assert.Equal([2u, 1u], sub.Route);
        Assert.Equal(FarmingRouteSource.Pinned, sub.RouteSource);
        Assert.Equal(TimeSpan.FromHours(20), sub.VoyageDuration);
        Assert.Equal(TimeSpan.FromHours(4), sub.CollectionDelay);
        Assert.Equal(100_000m, sub.GilPerDay);
    }

    [Theory]
    [InlineData("route")]
    [InlineData("build")]
    [InlineData("sector")]
    [InlineData("duration")]
    [InlineData("delay")]
    public void UnusableSetupProducesAnUnavailableEstimate(string missing)
    {
        var sub = Sub(1, 10);
        var prefs = new Dictionary<string, FcPreferences>();
        var catalog = new ProjectionCatalog();
        if (missing == "route") sub = sub with { CurrentRoute = [], CurrentVoyageKnown = false };
        if (missing == "build") sub = sub with { BuildParts = SubmarineBuildParts.Empty };
        if (missing == "sector") sub = sub with { CurrentRoute = [1, 99] };
        if (missing == "duration") catalog.Duration = _ => TimeSpan.Zero;
        if (missing == "delay") prefs["01"] = new() { Submarines = { [1] = new() { CollectionDelayMinutes = -1 } } };
        var result = Calculate([Fc(1, sub)], prefs, catalog).FreeCompanies[0];
        Assert.Null(result.Totals.GilPerDay);
        Assert.NotNull(result.Submarines[0].UnavailableReason);
    }

    [Fact]
    public void PartialSubtotalPreservesZeroAndDoesNotScaleUpToMissingFarmers()
    {
        var fcs = new[] { Fc(1, Sub(1, 10), Sub(2, 0) with { CurrentRoute = [3] }), Fc(2, Sub(1, 10, 0)) };
        var result = Calculate(fcs);
        var totals = IncomeProjectionPresentation.Summarize(result.FreeCompanies);
        Assert.Equal(new IncomeProjectionTotals(50_000m, 2, 3), totals);
        Assert.True(totals.IsPartial);
        Assert.Equal(18_250_000m, totals.ProjectedGil(IncomeProjectionHorizon.Days365));
    }

    [Fact]
    public void FractionsArePreservedThroughAggregation()
    {
        var sub = Sub(1, 10, 0);
        sub = sub with { VoyageHistory = sub.VoyageHistory.Select((voyage, index) => index == 0
            ? voyage with { Items = [new SalvageItemTotal(22500, "Salvage", 1, 1)] } : voyage).ToArray() };
        var result = Calculate([Fc(1, sub, sub with { SubmarineId = 2 })]);
        Assert.All(result.FreeCompanies[0].Submarines, estimate => Assert.Equal(0.05m, estimate.GilPerDay));
        Assert.Equal(36.5m, result.FreeCompanies[0].Totals.ProjectedGil(IncomeProjectionHorizon.Days365));
    }

    [Fact]
    public void ScopeAndFavoritesChangePresentationWithoutChangingLearnedRates()
    {
        var result = Calculate([Fc(1, Sub(1, 5, 100_000)), Fc(2, Sub(1, 5, 200_000)), Fc(3, Sub(1, 10, 300_000))]);
        var overview = IncomeProjectionPresentation.Select(result, null, id => id == "01");
        Assert.Equal(["01", "03", "02"], overview.Select(fc => fc.FcIdKey));
        var single = Assert.Single(IncomeProjectionPresentation.Select(result, "01", _ => false));
        Assert.Same(result.FreeCompanies[0], single);
        Assert.Equal(112_500m, single.Totals.GilPerDay);
        Assert.Equal(20, single.Submarines[0].Sample.ReturnCount);
    }

    [Fact]
    public void ScopedFcWithoutFarmersIsRetainedForItsEmptyState()
    {
        var result = Calculate([Fc(1, Sub(1, 10))], new Dictionary<string, FcPreferences> { ["01"] = Prefs((1, SubmarineAssignment.Leveling)) });
        Assert.Empty(IncomeProjectionPresentation.Select(result, null, _ => false));
        Assert.Empty(Assert.Single(IncomeProjectionPresentation.Select(result, "01", _ => false)).Submarines);
    }

    [Fact]
    public void EstimateIgnoresCurrentVoyageTimingAndFuelStock()
    {
        var prefs = new Dictionary<string, FcPreferences>
        {
            ["01"] = new() { FuelStockMode = FuelStockMode.Manual, ManualCeruleumTanks = 0,
                Submarines = { [1] = new() { PinnedFarmingRoute = [1, 2] } } },
        };
        foreach (var returnAt in new[] { DateTimeOffset.MinValue, Now.AddDays(-20), Now.AddDays(2) })
        {
            var sub = Sub(1, 10) with { ReturnAtUtc = returnAt, CurrentVoyageKnown = false };
            Assert.Equal(50_000m, Calculate([Fc(1, sub)], prefs).FreeCompanies[0].Totals.GilPerDay);
        }
    }

    [Fact]
    public void HistoryUnavailableIsNotReportedAsRecordedZero()
    {
        var fc = Fc(1, Sub(1, 0)) with { IncomeHistory = new(IncomeHistoryReadStatus.Unavailable, "Missing loot table") };
        var result = Calculate([fc]);
        Assert.Contains("Missing loot table", Assert.Single(result.HistoryNotices));
        Assert.Null(result.FreeCompanies[0].Totals.GilPerDay);
    }
}

internal static class IncomeProjectionTestData
{
    internal static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    internal static EtaSettings Settings => EtaSettings.CreateDefault() with { TargetRank = 90, CollectionDelayMinutes = 120 };

    internal static IncomeProjectionResult Calculate(IReadOnlyList<FcState> fcs,
        IReadOnlyDictionary<string, FcPreferences>? preferences = null, ProjectionCatalog? catalog = null, DateTimeOffset? now = null)
    {
        catalog ??= new ProjectionCatalog();
        return IncomeProjectionCalculator.Calculate(fcs, preferences ?? new Dictionary<string, FcPreferences>(), Settings,
            catalog, catalog, now ?? Now);
    }

    internal static FcPreferences Prefs(params (long Id, SubmarineAssignment Role)[] roles)
        => new() { Submarines = roles.ToDictionary(role => role.Id, role => new SubmarinePreferences { Assignment = role.Role }) };

    internal static FcState Fc(byte id, params SubmarineState[] submarines)
        => new([id], $"FC {id}", "World", new HashSet<uint>(), new HashSet<uint>(),
            submarines.Select(sub => sub with { FcId = [id], VoyageHistory = sub.VoyageHistory
                .Select(v => v with { FcIdKey = Convert.ToHexString([id]), SubmarineId = sub.SubmarineId }).ToArray() }).ToArray())
        { IncomeHistory = IncomeHistoryReadState.Available };

    internal static SubmarineState Sub(long id, int count, long gil = 100_000)
        => new([1], id, $"Sub {id}", 100, 0, 1000, new(1, 2, 3, 4), Now.AddDays(1), [1, 2], true, [])
        { VoyageHistory = Enumerable.Range(1, count).Select(day => Voyage(id, Now.AddDays(-day), gil)).ToArray() };

    internal static VoyageObservation Voyage(long id, DateTimeOffset returned, long gil)
        => new("01", 1, id, returned, [1, 2], 100, 100, 110, 120,
            gil == 0 ? [] : [new SalvageItemTotal(22500, "Salvage", 1, gil)]);

    internal sealed class ProjectionCatalog : ISubmarineCatalog, IRouteOperationalCatalog
    {
        public Func<IReadOnlyList<uint>, TimeSpan> Duration { get; set; } = _ => TimeSpan.FromHours(46);
        public Func<SubmarineBuildParts, int, SubmarineBuild?>? Build { get; set; }
        public Func<IReadOnlyList<uint>, SubmarineBuild, TimeSpan>? BuildDuration { get; set; }
        public HashSet<uint> KnownSectors { get; set; } = [1, 2, 3, 4];
        public Dictionary<(SubmarineBuildParts Parts, int Rank), int> BuildCalls { get; } = [];
        public int AnalysisCalls { get; private set; }
        public int MaximumRank => 200;
        public IReadOnlyList<UnlockRule> UnlockRules => [];
        public SubmarineBuild ResolveBuild(string buildCode, int rank) => new(buildCode, rank, 100, 110, 120, 100, 100);
        public SubmarineBuild? ResolveBuild(SubmarineBuildParts parts, int rank)
        {
            var key = (parts, rank);
            BuildCalls[key] = BuildCalls.GetValueOrDefault(key) + 1;
            return parts == SubmarineBuildParts.Empty ? null
                : Build is null ? ResolveBuild("SSUW", rank) : Build(parts, rank);
        }
        public RouteSearchResult FindBestRoute(RouteSearchRequest request) => throw new InvalidOperationException("Income must not run leveling route searches.");
        public uint CalculateExp(IReadOnlyList<uint> route, SubmarineBuild build, ExpMode expMode) => throw new InvalidOperationException();
        public TimeSpan CalculateDuration(IReadOnlyList<uint> route, SubmarineBuild build) => BuildDuration?.Invoke(route, build) ?? Duration(route);
        public (int Rank, uint CurrentExp, uint NextLevelExp) ApplyExp(int rank, uint exp, uint gained, int target) => throw new InvalidOperationException();
        public string PointName(uint point) => point.ToString();
        public int GetPointRequiredRank(uint point) => 1;
        public RouteFuelProfile CalculateFuel(IReadOnlyCollection<uint> sectors)
            => new(sectors.Distinct().Count() * 5, sectors.All(KnownSectors.Contains), sectors.Where(id => !KnownSectors.Contains(id)).ToArray());
        public OrderedRouteOperationalProfile AnalyzeOrderedRoute(IReadOnlyList<uint> route, SubmarineBuild build)
        {
            AnalysisCalls++;
            return new(route, CalculateFuel(route), CalculateDuration(route, build));
        }
    }
}
