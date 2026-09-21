using SubmarineEtaPlanner.Planner;
using Xunit;
using static SubmarineEtaPlanner.Tests.IncomeProjectionTestData;
using static SubmarineEtaPlanner.Tests.PreviousRankProjectionTestData;

namespace SubmarineEtaPlanner.Tests;

public sealed class IncomeProjectionPreviousRankTests
{
    [Theory]
    [InlineData(10, 10, 10, 10, IncomeProjectionMatchKind.ExactStats, IncomeProjectionSampleSource.Own, 100_000)]
    [InlineData(5, 5, 10, 10, IncomeProjectionMatchKind.ExactStats, IncomeProjectionSampleSource.Pooled, 150_000)]
    [InlineData(0, 0, 10, 10, IncomeProjectionMatchKind.PreviousRank, IncomeProjectionSampleSource.Own, 120_000)]
    [InlineData(0, 0, 5, 5, IncomeProjectionMatchKind.PreviousRank, IncomeProjectionSampleSource.Pooled, 180_000)]
    public void SelectsFirstSufficientSampleInRequiredOrder(int exactOwn, int exactDonor, int previousOwn, int previousDonor,
        IncomeProjectionMatchKind kind, IncomeProjectionSampleSource source, long average)
    {
        var result = Calculate([Fc(1, Farmer(1, exactOwn, previousOwn)),
            Fc(2, Farmer(2, exactDonor, previousDonor, 200_000, 240_000))], catalog: RankCatalog());
        var sub = result.FreeCompanies[0].Submarines[0];
        Assert.Equal(kind, sub.Sample.MatchKind);
        Assert.Equal(source, sub.Sample.Source);
        Assert.Equal((decimal)average, sub.AverageGilPerVoyage);
        Assert.Equal(10, sub.Sample.ReturnCount);
        Assert.Equal(source == IncomeProjectionSampleSource.Own ? 1 : 2, sub.Sample.ContributingFcCount);
        Assert.Equal(exactOwn + exactDonor, sub.ExactMatchingReturnCount);
        Assert.Equal(previousOwn + previousDonor, sub.PreviousRankMatchingReturnCount);
        Assert.Equal(CurrentStats, sub.CurrentStats);
        Assert.Equal(kind == IncomeProjectionMatchKind.PreviousRank ? PreviousStats : CurrentStats, sub.Sample.MatchedStats);
        Assert.Equal(kind == IncomeProjectionMatchKind.PreviousRank ? 143 : (int?)null, sub.Sample.ReferenceRank);
        Assert.Equal(kind == IncomeProjectionMatchKind.PreviousRank, sub.IsApproximate);
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    public void MinimumAppliesToPreviousRankIncludingValidZero(int count, bool available)
    {
        var fc = Calculate([Fc(1, Farmer(1, previous: count, previousGil: 0))], catalog: RankCatalog()).FreeCompanies[0];
        var sub = fc.Submarines[0];
        Assert.Equal(available, sub.IsAvailable);
        Assert.Equal(available, sub.IsApproximate);
        Assert.Equal(available ? 0m : (decimal?)null, fc.Totals.GilPerDay);
        Assert.Equal(available ? 1 : 0, fc.Totals.ApproximateSubmarines);
        Assert.Equal(0, sub.ExactMatchingReturnCount);
        Assert.Equal(count, sub.PreviousRankMatchingReturnCount);
        Assert.Equal(available ? count : 0, sub.Sample.ReturnCount);
        Assert.Equal(available ? null : "Insufficient matching history", sub.UnavailableReason);
    }

    [Fact]
    public void DoesNotCombineInsufficientExactAndPreviousSamples()
    {
        var sub = Calculate([Fc(1, Farmer(1, exact: 9, previous: 9))], catalog: RankCatalog()).FreeCompanies[0].Submarines[0];
        Assert.False(sub.IsAvailable);
        Assert.Equal(9, sub.ExactMatchingReturnCount);
        Assert.Equal(9, sub.PreviousRankMatchingReturnCount);
        Assert.Equal(9, sub.Sample.ReturnCount);
    }

    [Fact]
    public void PoolsOnceAcrossVisibleDonorsRegardlessOfTheirCurrentRole()
    {
        var donor = Farmer(2, previous: 4) with { Rank = 100 };
        donor = donor with { VoyageHistory = donor.VoyageHistory.Concat(donor.VoyageHistory).ToArray() };
        var prefs = new Dictionary<string, FcPreferences>
        {
            ["02"] = Prefs((2, SubmarineAssignment.Paused)),
            ["03"] = Prefs((3, SubmarineAssignment.Leveling)),
            ["04"] = new() { Hidden = true },
        };
        var result = Calculate([Fc(1, Farmer(1, previous: 1)), Fc(2, donor), Fc(3, Farmer(3, previous: 5)),
            Fc(4, Farmer(4, previous: 20, previousGil: 9_999_999))], prefs, RankCatalog());
        var estimate = result.FreeCompanies[0].Submarines[0];
        Assert.True(estimate.IsApproximate);
        Assert.Equal(10, estimate.Sample.ReturnCount);
        Assert.Equal(3, estimate.Sample.ContributingFcCount);
        Assert.Equal(120_000m, estimate.AverageGilPerVoyage);
        var scoped = IncomeProjectionPresentation.Select(result, "01", _ => false);
        Assert.Same(estimate, Assert.Single(scoped).Submarines[0]);
        Assert.Equal(estimate.GilPerDay, IncomeProjectionPresentation.Summarize(scoped).GilPerDay);
    }

    [Theory]
    [InlineData("sectors")]
    [InlineData("surveillance")]
    [InlineData("retrieval")]
    [InlineData("favor")]
    [InlineData("older-rank")]
    [InlineData("current-rank")]
    [InlineData("higher-rank")]
    public void RejectsIncompatibleHistoricalStatsSectorsAndRank(string mismatch)
    {
        var sub = Farmer(1, previous: 10);
        sub = sub with { VoyageHistory = sub.VoyageHistory.Select(v => mismatch switch
        {
            "sectors" => v with { SectorIds = [1] },
            "surveillance" => v with { Surveillance = v.Surveillance + 1 },
            "retrieval" => v with { Retrieval = v.Retrieval + 1 },
            "favor" => v with { Favor = v.Favor + 1 },
            "older-rank" => v with { Rank = 142 },
            "current-rank" => v with { Rank = 144 },
            _ => v with { Rank = 145 },
        }).ToArray() };
        var estimate = Calculate([Fc(1, sub)], catalog: RankCatalog()).FreeCompanies[0].Submarines[0];
        Assert.False(estimate.IsAvailable);
        Assert.Equal(0, estimate.PreviousRankMatchingReturnCount);
    }

    [Fact]
    public void PreviousHistoryWindowIsInclusiveAndFutureReturnsAreExcluded()
    {
        var sub = Farmer(1, previous: 8);
        var voyage = sub.VoyageHistory[0];
        sub = sub with { VoyageHistory = sub.VoyageHistory.Concat(new[]
        {
            voyage with { ReturnAtUtc = Now.AddDays(-90) }, voyage with { ReturnAtUtc = Now },
            voyage with { ReturnAtUtc = Now.AddDays(-90).AddTicks(-1) }, voyage with { ReturnAtUtc = Now.AddTicks(1) },
        }).ToArray() };
        var result = Calculate([Fc(1, sub)], catalog: RankCatalog());
        var estimate = result.FreeCompanies[0].Submarines[0];
        Assert.True(estimate.IsApproximate);
        Assert.Equal(10, estimate.Sample.ReturnCount);
        Assert.Equal(Now.AddDays(-90), estimate.Sample.FirstReturnAtUtc);
        Assert.Equal(Now, estimate.Sample.LastReturnAtUtc);
        Assert.Equal(Now.AddTicks(1), result.NextRefreshAtUtc);
    }

    [Fact]
    public void UsesCurrentPartsPreviousStatsButCurrentSpeedOrderedRouteAndDelay()
    {
        var catalog = RankCatalog();
        catalog.BuildDuration = (route, build) => route.SequenceEqual(new uint[] { 2, 1 }) && build.Speed == 98
            ? TimeSpan.FromHours(20) : TimeSpan.FromHours(72);
        var prefs = new Dictionary<string, FcPreferences>
        {
            ["01"] = new() { Submarines = { [1] = new() { PinnedFarmingRoute = [2, 1], CollectionDelayMinutes = 240 } } },
        };
        var sub = Farmer(1, previous: 10);
        sub = sub with { VoyageHistory = sub.VoyageHistory.Select((v, i) => v with
        {
            SectorIds = [2, 1, 2], Items = i == 0 ? [] : v.Items,
        }).ToArray() };
        var estimate = Calculate([Fc(1, sub)], prefs, catalog).FreeCompanies[0].Submarines[0];
        Assert.True(estimate.IsApproximate);
        Assert.Equal(FarmingRouteSource.Pinned, estimate.RouteSource);
        Assert.Equal(TimeSpan.FromHours(24), estimate.FullCycleDuration);
        Assert.Equal(108_000m, estimate.AverageGilPerVoyage);
        Assert.Equal(108_000m, estimate.GilPerDay);
        Assert.Equal(39_420_000m, estimate.ProjectedGil(IncomeProjectionHorizon.Days365));
        Assert.Equal(1, catalog.BuildCalls[(sub.BuildParts, 143)]);
    }

    [Theory]
    [InlineData("route")]
    [InlineData("build")]
    [InlineData("sector")]
    [InlineData("duration")]
    [InlineData("delay")]
    public void FallbackNeverBypassesMissingCurrentSetup(string missing)
    {
        var sub = Farmer(1, previous: 10);
        var catalog = RankCatalog();
        var prefs = new Dictionary<string, FcPreferences>();
        if (missing == "route") sub = sub with { CurrentRoute = [], CurrentVoyageKnown = false };
        if (missing == "build") sub = sub with { BuildParts = SubmarineBuildParts.Empty };
        if (missing == "sector") sub = sub with { CurrentRoute = [1, 99] };
        if (missing == "duration") catalog.Duration = _ => TimeSpan.Zero;
        if (missing == "delay") prefs["01"] = new() { Submarines = { [1] = new() { CollectionDelayMinutes = -1 } } };
        var estimate = Calculate([Fc(1, sub)], prefs, catalog).FreeCompanies[0].Submarines[0];
        Assert.False(estimate.IsAvailable);
        Assert.False(estimate.IsApproximate);
        Assert.NotNull(estimate.UnavailableReason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void UnresolvedPreviousBuildRetainsExactResult(int exact)
    {
        var catalog = RankCatalog();
        var resolve = catalog.Build!;
        catalog.Build = (parts, rank) => rank == 143 ? null : resolve(parts, rank);
        var estimate = Calculate([Fc(1, Farmer(1, exact, 10))], catalog: catalog).FreeCompanies[0].Submarines[0];
        Assert.Equal(exact == 10, estimate.IsAvailable);
        Assert.Null(estimate.PreviousRankMatchingReturnCount);
        Assert.False(estimate.IsApproximate);
    }

    [Fact]
    public void NeverResolvesRankBelowOne()
    {
        var catalog = RankCatalog();
        var sub = Farmer(1, exact: 10, previous: 10) with { Rank = 1 };
        var prefs = new Dictionary<string, FcPreferences> { ["01"] = Prefs((1, SubmarineAssignment.Farming)) };
        _ = Calculate([Fc(1, sub)], prefs, catalog);
        Assert.All(catalog.BuildCalls.Keys, key => Assert.True(key.Rank >= 1));
    }

    [Fact]
    public void ResolvesPreviousBuildOnceForIdenticalPartsAndRank()
    {
        var catalog = RankCatalog();
        _ = Calculate([Fc(1, Farmer(1, previous: 10), Farmer(2), Farmer(3), Farmer(4))], catalog: catalog);
        Assert.Equal(1, catalog.BuildCalls[(Parts, 143)]);
    }

    [Fact]
    public void ChangedCurrentPartsRejectIncompatiblePreviousStats()
    {
        var catalog = RankCatalog();
        var resolve = catalog.Build!;
        catalog.Build = (parts, rank) => resolve(parts, rank)! with { Surveillance = parts.Hull == Parts.Hull ? 203 : 250 };
        var sub = Farmer(1, previous: 10) with { BuildParts = Parts with { Hull = 27 } };
        var estimate = Calculate([Fc(1, sub)], catalog: catalog).FreeCompanies[0].Submarines[0];
        Assert.False(estimate.IsAvailable);
        Assert.Equal(0, estimate.PreviousRankMatchingReturnCount);
    }

    [Fact]
    public void MixedTotalsCountZeroApproximationsAndKeepMissingFarmersPartial()
    {
        var unknown = Farmer(3) with { CurrentRoute = [3] };
        var zero = Farmer(2, previous: 10, previousGil: 0) with { CurrentRoute = [4] };
        zero = zero with { VoyageHistory = zero.VoyageHistory.Select(v => v with { SectorIds = [4u] }).ToArray() };
        var fc = Calculate([Fc(1, Farmer(1, exact: 10), zero, unknown)], catalog: RankCatalog()).FreeCompanies[0];
        Assert.Equal(new IncomeProjectionTotals(50_000m, 2, 3, 1), fc.Totals);
        Assert.Equal(1, fc.Totals.ExactSubmarines);
        Assert.True(fc.Totals.IsPartial);
        Assert.True(fc.Totals.IncludesApproximations);
        Assert.Equal("1 exact · 1 approximate", fc.Totals.MatchCoverage);
    }

    [Fact]
    public void MeowCuteTransitionRecoversCoverageWhileNewGlossRouteStaysUnavailable()
    {
        var catalog = RankCatalog();
        catalog.KnownSectors = [10, 12, 13, 15, 18, 26];
        SubmarineState OnRoute(SubmarineState sub, uint[] route) => sub with
        {
            CurrentRoute = route,
            VoyageHistory = sub.VoyageHistory.Select(v => v with { SectorIds = route.Reverse().ToArray() }).ToArray(),
        };
        var mrojz = new uint[] { 13, 18, 15, 10, 26 };
        var growers = new[] { Fc(1, OnRoute(Farmer(1, previous: 19), mrojz)), Fc(2, OnRoute(Farmer(1, previous: 19), mrojz)) };
        growers = growers.Select(fc => fc with { Submarines = fc.Submarines.Concat(Enumerable.Range(2, 3)
            .Select(id => OnRoute(Farmer(id, previous: 18) with { FcId = fc.FcId, Rank = 143 }, mrojz))).ToArray() }).ToArray();
        var gloss = Fc(3, Enumerable.Range(1, 4).Select(id =>
        {
            var sub = OnRoute(Farmer(id, exact: 1), [13, 18, 12, 15, 10]);
            return sub with
            {
                Rank = 94, BuildParts = new(11, 4, 13, 14),
                VoyageHistory = sub.VoyageHistory.Select(v => v with { Rank = 94, Surveillance = 133, Retrieval = 179, Favor = 139 }).ToArray(),
            };
        }).ToArray());
        var resolve = catalog.Build!;
        catalog.Build = (parts, rank) => rank == 94 ? new("WSCC", 94, 133, 179, 139, 100, 98) : resolve(parts, rank);
        var result = Calculate(growers.Append(gloss).ToArray(), catalog: catalog);
        Assert.All(result.FreeCompanies.Take(2), fc =>
        {
            Assert.Equal(4, fc.Totals.EstimatedSubmarines);
            Assert.Equal(1, fc.Totals.ApproximateSubmarines);
            Assert.False(fc.Totals.IsPartial);
        });
        var newRoute = result.FreeCompanies[2];
        Assert.Equal(0, newRoute.Totals.EstimatedSubmarines);
        Assert.All(newRoute.Submarines, sub => Assert.Equal(4, sub.ExactMatchingReturnCount));
    }
}

internal static class PreviousRankProjectionTestData
{
    internal static readonly SubmarineBuildParts Parts = new(23, 36, 25, 22);
    internal static readonly IncomeProjectionStats CurrentStats = new(203, 254, 223);
    internal static readonly IncomeProjectionStats PreviousStats = new(203, 252, 222);

    internal static ProjectionCatalog RankCatalog() => new()
    {
        Build = (parts, rank) =>
        {
            var stats = rank == 144 ? CurrentStats : PreviousStats;
            return new("S+C+U+S+", rank, stats.Surveillance, stats.Retrieval, stats.Favor, 100, rank == 144 ? 98 : 90);
        },
    };

    internal static SubmarineState Farmer(long id, int exact = 0, int previous = 0,
        long exactGil = 100_000, long previousGil = 120_000)
    {
        VoyageObservation Observation(int index, bool old)
        {
            var stats = old ? PreviousStats : CurrentStats;
            return Voyage(id, Now.AddDays(-(old ? 30 : 0) - index), old ? previousGil : exactGil) with
            {
                Rank = old ? 143 : 144, Surveillance = stats.Surveillance, Retrieval = stats.Retrieval, Favor = stats.Favor,
            };
        }
        return Sub(id, 0) with
        {
            Rank = 144, BuildParts = Parts,
            VoyageHistory = Enumerable.Range(1, exact).Select(i => Observation(i, false))
                .Concat(Enumerable.Range(1, previous).Select(i => Observation(i, true))).ToArray(),
        };
    }
}
