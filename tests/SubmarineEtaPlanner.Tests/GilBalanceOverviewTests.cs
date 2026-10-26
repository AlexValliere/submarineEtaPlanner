using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class GilBalanceOverviewTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-26T12:00:00Z");

    [Fact]
    public void ScopeExcludesHiddenUnassignedAndUntrackedWalletsAndChests()
    {
        var balances = new GilBalanceSnapshot(
            [Wallet(1, 10, 100), Wallet(2, 20, 200), Wallet(3, 30, 300), Wallet(4, 0, 400), Wallet(5, null, 500)],
            [new(10, 1_000, Now), new(20, 2_000, Now), new(30, 3_000, Now)]);

        var overview = GilBalanceOverview.Create([10, 20], balances);

        Assert.Equal(300, overview.WalletGil);
        Assert.Equal(3_000, overview.ChestGil);
        Assert.Equal(3_300, overview.RecordedGil);
        Assert.Equal(2, overview.VisibleFcCount);
        Assert.Equal(2, overview.ObservedFcCount);
        Assert.Equal(2, overview.ObservedWalletCount);
        Assert.Equal(2, overview.ObservedChestCount);
        Assert.False(overview.IsPartial);
    }

    [Fact]
    public void LatestWalletAssociationIsResolvedBeforeVisibleScopeIsApplied()
    {
        var balances = new GilBalanceSnapshot(
            [Wallet(1, 10, 100) with { ObservedAtUtc = Now.AddMinutes(-1) }, Wallet(1, 20, 110)], []);

        var overview = GilBalanceOverview.Create([10], balances);

        Assert.Null(overview.RecordedGil);
        Assert.Equal(0, overview.ObservedWalletCount);
        Assert.True(overview.IsPartial);
    }

    [Fact]
    public void DuplicateFcReferencesAndSharedChestsAreCountedOnce()
    {
        var balances = new GilBalanceSnapshot(
            [Wallet(1, 10, 999_999_999), Wallet(2, 10, 999_999_999), Wallet(3, 10, 999_999_999)],
            [new(10, 100, Now.AddMinutes(-1)), new(10, 200, Now)]);

        var overview = GilBalanceOverview.Create([10, 10], balances);

        Assert.Equal(1, overview.VisibleFcCount);
        Assert.Equal(3, overview.ObservedWalletCount);
        Assert.Equal(1, overview.ObservedChestCount);
        Assert.Equal(3_000_000_197L, overview.RecordedGil);
    }

    [Fact]
    public void MissingSourcesProducePartialCoverageAndRecordedZeroRemainsKnown()
    {
        var balances = new GilBalanceSnapshot([Wallet(1, 10, 0)], [new(10, 0, Now)]);
        var knownZero = GilBalanceOverview.Create([10], balances);
        var partial = GilBalanceOverview.Create([10, 20, null], balances);
        var unknown = GilBalanceOverview.Create([20], balances);

        Assert.Equal(0, knownZero.RecordedGil);
        Assert.False(knownZero.IsPartial);
        Assert.Equal(0, partial.RecordedGil);
        Assert.Equal(3, partial.VisibleFcCount);
        Assert.Equal(1, partial.ObservedFcCount);
        Assert.True(partial.IsPartial);
        Assert.Null(unknown.WalletGil);
        Assert.Null(unknown.ChestGil);
        Assert.Null(unknown.RecordedGil);
    }

    [Fact]
    public void UnknownOrEmptyScopeDoesNotExposeOutOfScopeObservations()
    {
        var balances = new GilBalanceSnapshot([Wallet(1, 10, 100)], [new(10, 200, Now)]);
        var loading = GilBalanceOverview.Create([10], balances, scopeKnown: false);
        var empty = GilBalanceOverview.Create([], balances);

        Assert.False(loading.ScopeKnown);
        Assert.Null(loading.RecordedGil);
        Assert.Null(loading.OldestObservationAtUtc);
        Assert.Equal(0, loading.VisibleFcCount);
        Assert.True(empty.ScopeKnown);
        Assert.Equal(0, empty.VisibleFcCount);
        Assert.Null(empty.RecordedGil);
        Assert.False(empty.IsPartial);
    }

    [Fact]
    public void FreshnessUsesOldestIncludedSnapshotAndDoesNotChangeTheObservations()
    {
        var wallet = Wallet(1, 10, 100) with { ObservedAtUtc = Now.AddHours(-2), IsLive = true };
        var balances = new GilBalanceSnapshot([wallet, Wallet(2, 20, 200) with { ObservedAtUtc = Now.AddDays(-5) }],
            [new(10, 500, Now.AddMinutes(-1))]);

        var overview = GilBalanceOverview.Create([10], balances);

        Assert.Equal(Now.AddHours(-2), overview.OldestObservationAtUtc);
        Assert.Equal(Now.AddHours(-2), wallet.ObservedAtUtc);
        Assert.True(wallet.IsLive);
    }

    private static CharacterGilObservation Wallet(ulong id, ulong? fcId, long gil)
        => new(id, fcId, $"Character {id}", "Cerberus", gil, Now, false);
}
