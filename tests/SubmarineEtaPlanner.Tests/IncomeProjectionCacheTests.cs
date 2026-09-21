using SubmarineEtaPlanner.Planner;
using Xunit;
using static SubmarineEtaPlanner.Tests.IncomeProjectionTestData;

namespace SubmarineEtaPlanner.Tests;

public sealed class IncomeProjectionCacheTests
{
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
