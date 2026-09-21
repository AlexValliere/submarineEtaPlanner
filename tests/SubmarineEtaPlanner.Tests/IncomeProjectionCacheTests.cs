using SubmarineEtaPlanner.Planner;
using Xunit;
using static SubmarineEtaPlanner.Tests.IncomeProjectionTestData;
using static SubmarineEtaPlanner.Tests.PreviousRankProjectionTestData;

namespace SubmarineEtaPlanner.Tests;

public sealed class IncomeProjectionCacheTests
{
    [Fact]
    public void FallbackToggleInvalidatesWithoutChangingTheHistorySnapshot()
    {
        var cache = new IncomeProjectionCache();
        var catalog = RankCatalog();
        var prefs = new Dictionary<string, FcPreferences>();
        FcState[] snapshot = [Fc(1, Farmer(1, previous: 10))];
        var first = cache.Get(snapshot, prefs, Settings, catalog, catalog, Now);
        Assert.True(first.FreeCompanies[0].Submarines[0].IsApproximate);
        var disabled = cache.Get(snapshot, prefs, Settings, catalog, catalog, Now, allowPreviousRankFallback: false);
        Assert.False(disabled.FreeCompanies[0].Submarines[0].IsAvailable);
        Assert.Null(disabled.FreeCompanies[0].Submarines[0].PreviousRankMatchingReturnCount);
        var enabled = cache.Get(snapshot, prefs, Settings, catalog, catalog, Now);
        Assert.True(enabled.FreeCompanies[0].Submarines[0].IsApproximate);
        Assert.Equal(3, cache.BuildCount);
    }

    [Fact]
    public void HistoryPromotesApproximationToExactEvenWhenRateDoesNotChange()
    {
        var cache = new IncomeProjectionCache();
        var chartCache = new IncomeProjectionChartCache();
        var catalog = RankCatalog();
        var prefs = new Dictionary<string, FcPreferences>();
        var oldFc = Fc(1, Farmer(1, exact: 9, previous: 10, exactGil: 120_000));
        var newFc = Fc(1, Farmer(1, exact: 10, previous: 10, exactGil: 120_000));
        Assert.Equal(FcDataFingerprint.Create(oldFc), FcDataFingerprint.Create(newFc));
        var first = cache.Get([oldFc], prefs, Settings, catalog, catalog, Now).FreeCompanies[0];
        var firstChart = chartCache.Get(true, first.Totals, IncomeProjectionHorizon.Days365, Now, TimeZoneInfo.Utc)!;
        var second = cache.Get([newFc], prefs, Settings, catalog, catalog, Now).FreeCompanies[0];
        var secondChart = chartCache.Get(true, second.Totals, IncomeProjectionHorizon.Days365, Now, TimeZoneInfo.Utc)!;
        Assert.Equal(first.Totals.GilPerDay, second.Totals.GilPerDay);
        Assert.True(first.Totals.IncludesApproximations);
        Assert.False(second.Totals.IncludesApproximations);
        Assert.Equal(IncomeProjectionMatchKind.ExactStats, second.Submarines[0].Sample.MatchKind);
        Assert.Null(second.Submarines[0].Sample.ReferenceRank);
        Assert.NotSame(firstChart, secondChart);
        Assert.Equal(firstChart.EstimatedGil, secondChart.EstimatedGil);
        Assert.False(secondChart.Totals.IncludesApproximations);
    }

    [Fact]
    public void PreviousRankExpiryFutureEntryAndRollbackReevaluateEligibility()
    {
        var cache = new IncomeProjectionCache();
        var catalog = RankCatalog();
        var prefs = new Dictionary<string, FcPreferences>();
        var sub = Farmer(1, previous: 9);
        var sample = sub.VoyageHistory[0];
        sub = sub with { VoyageHistory = sub.VoyageHistory.Concat(new[]
        {
            sample with { ReturnAtUtc = Now.AddDays(-90) }, sample with { ReturnAtUtc = Now.AddSeconds(10) },
        }).ToArray() };
        FcState[] snapshot = [Fc(1, sub)];
        Assert.True(cache.Get(snapshot, prefs, Settings, catalog, catalog, Now).FreeCompanies[0].Submarines[0].IsApproximate);
        Assert.False(cache.Get(snapshot, prefs, Settings, catalog, catalog, Now.AddTicks(1)).FreeCompanies[0].Submarines[0].IsAvailable);
        var future = cache.Get(snapshot, prefs, Settings, catalog, catalog, Now.AddSeconds(10));
        Assert.True(future.FreeCompanies[0].Submarines[0].IsApproximate);
        var rollback = cache.Get(snapshot, prefs, Settings, catalog, catalog, Now.AddSeconds(5));
        Assert.False(rollback.FreeCompanies[0].Submarines[0].IsAvailable);
        Assert.Equal(4, cache.BuildCount);
    }

    [Fact]
    public void ApproximationScopeAndFavoritesReuseCachedHistory()
    {
        var cache = new IncomeProjectionCache();
        var catalog = RankCatalog();
        var prefs = new Dictionary<string, FcPreferences>();
        FcState[] snapshot = [Fc(1, Farmer(1, previous: 5)), Fc(2, Farmer(2, previous: 5))];
        var first = cache.Get(snapshot, prefs, Settings, catalog, catalog, Now);
        prefs["02"] = new() { Favorite = true };
        foreach (var scope in new[] { "01", "02", null })
        {
            var current = cache.Get(snapshot, prefs, Settings, catalog, catalog, Now.AddSeconds(1));
            Assert.Same(first, current);
            var scoped = IncomeProjectionPresentation.Select(current, scope, id => id == "02");
            Assert.All(scoped.SelectMany(fc => fc.Submarines), sub => Assert.Equal(10, sub.Sample.ReturnCount));
        }
        Assert.Equal(1, cache.BuildCount);
        prefs["02"].Hidden = true;
        var hidden = cache.Get(snapshot, prefs, Settings, catalog, catalog, Now.AddSeconds(2));
        Assert.False(hidden.FreeCompanies[0].Submarines[0].IsAvailable);
        Assert.Equal(2, cache.BuildCount);
    }

    [Fact]
    public void ScopeFavoritesAndFuelDoNotRebuildTheSamplePool()
    {
        var cache = new IncomeProjectionCache();
        FcState[] source = [Fc(1, Sub(1, 5)), Fc(2, Sub(1, 5))];
        var preferences = new Dictionary<string, FcPreferences>();
        var catalog = new ProjectionCatalog();
        var result = cache.Get(source, preferences, Settings, catalog, catalog, Now);
        preferences["01"] = new() { Favorite = true, ManualCeruleumTanks = 100, FuelStockMode = FuelStockMode.Manual };
        foreach (var scope in new[] { "01", "02", null })
        {
            var cached = cache.Get(source, preferences, Settings, catalog, catalog, Now.AddSeconds(10));
            Assert.Same(result, cached);
            _ = IncomeProjectionPresentation.Select(cached, scope, id => id == "01");
        }
        Assert.Equal(1, cache.BuildCount);
        Assert.Equal(2, catalog.AnalysisCalls);
    }

    [Fact]
    public void NewHistorySnapshotInvalidatesEvenWhenLevelingFingerprintIsUnchanged()
    {
        var cache = new IncomeProjectionCache();
        var preferences = new Dictionary<string, FcPreferences>();
        var catalog = new ProjectionCatalog();
        var oldFc = Fc(1, Sub(1, 9));
        var newFc = Fc(1, Sub(1, 10));
        Assert.Equal(FcDataFingerprint.Create(oldFc), FcDataFingerprint.Create(newFc));
        var first = cache.Get([oldFc], preferences, Settings, catalog, catalog, Now);
        var second = cache.Get([newFc], preferences, Settings, catalog, catalog, Now);
        Assert.Null(first.FreeCompanies[0].Totals.GilPerDay);
        Assert.Equal(50_000m, second.FreeCompanies[0].Totals.GilPerDay);
        Assert.Equal(2, cache.BuildCount);
    }

    [Theory]
    [InlineData("visibility")]
    [InlineData("target")]
    [InlineData("assignment")]
    [InlineData("route")]
    [InlineData("delay")]
    [InlineData("global-target")]
    [InlineData("global-delay")]
    public void SavedInputsInvalidateResults(string change)
    {
        var cache = new IncomeProjectionCache();
        var preferences = new Dictionary<string, FcPreferences> { ["01"] = new() { Submarines = { [1] = new() } } };
        FcState[] source = [Fc(1, Sub(1, 10))];
        var catalog = new ProjectionCatalog();
        var settings = Settings;
        var first = cache.Get(source, preferences, settings, catalog, catalog, Now);
        switch (change)
        {
            case "visibility": preferences["01"].Hidden = true; break;
            case "target": preferences["01"].TargetRankOverride = 110; break;
            case "assignment": preferences["01"].Submarines[1].Assignment = SubmarineAssignment.Paused; break;
            case "route": preferences["01"].Submarines[1].PinnedFarmingRoute = [3]; break;
            case "delay": preferences["01"].Submarines[1].CollectionDelayMinutes = 180; break;
            case "global-target": settings = settings with { TargetRank = 110 }; break;
            case "global-delay": settings = settings with { CollectionDelayMinutes = 300 }; break;
        }
        var second = cache.Get(source, preferences, settings, catalog, catalog, Now.AddSeconds(1));
        Assert.NotSame(first, second);
        Assert.NotEqual(IncomeProjectionPresentation.Summarize(first.FreeCompanies), IncomeProjectionPresentation.Summarize(second.FreeCompanies));
        Assert.Equal(2, cache.BuildCount);
    }

    [Fact]
    public void InclusiveWindowExpiresOneTickLaterAndClockRollbackRebuilds()
    {
        var cache = new IncomeProjectionCache();
        var catalog = new ProjectionCatalog();
        var prefs = new Dictionary<string, FcPreferences>();
        var sub = Sub(1, 9) with { VoyageHistory = Sub(1, 9).VoyageHistory.Append(Voyage(1, Now.AddDays(-90), 100_000)).ToArray() };
        FcState[] source = [Fc(1, sub)];
        var first = cache.Get(source, prefs, Settings, catalog, catalog, Now);
        Assert.Equal(Now.AddTicks(1), first.NextRefreshAtUtc);
        Assert.Equal(50_000m, first.FreeCompanies[0].Totals.GilPerDay);
        var expired = cache.Get(source, prefs, Settings, catalog, catalog, Now.AddTicks(1));
        Assert.Null(expired.FreeCompanies[0].Totals.GilPerDay);
        var rollback = cache.Get(source, prefs, Settings, catalog, catalog, Now);
        Assert.Equal(50_000m, rollback.FreeCompanies[0].Totals.GilPerDay);
        Assert.Equal(3, cache.BuildCount);
    }

    [Fact]
    public void FutureReturnEntersAtItsBoundary()
    {
        var cache = new IncomeProjectionCache();
        var catalog = new ProjectionCatalog();
        var prefs = new Dictionary<string, FcPreferences>();
        var boundary = Now.AddSeconds(10);
        var sub = Sub(1, 9) with { VoyageHistory = Sub(1, 9).VoyageHistory.Append(Voyage(1, boundary, 100_000)).ToArray() };
        FcState[] source = [Fc(1, sub)];
        var first = cache.Get(source, prefs, Settings, catalog, catalog, Now);
        Assert.Equal(boundary, first.NextRefreshAtUtc);
        Assert.Null(first.FreeCompanies[0].Totals.GilPerDay);
        Assert.Same(first, cache.Get(source, prefs, Settings, catalog, catalog, boundary.AddTicks(-1)));
        Assert.Equal(50_000m, cache.Get(source, prefs, Settings, catalog, catalog, boundary).FreeCompanies[0].Totals.GilPerDay);
    }
}
