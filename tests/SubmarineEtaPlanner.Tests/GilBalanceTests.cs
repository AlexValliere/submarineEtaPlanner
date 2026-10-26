using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class GilBalanceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-26T10:00:00Z");

    [Fact]
    public void LatestCharacterAssociationAndSingleChestAreCountedOnce()
    {
        var snapshot = new GilBalanceSnapshot(
            [Wallet(1, 10, 100, -2), Wallet(1, 20, 90, -1), Wallet(2, 20, 0, 0)],
            [new(20, 500, Now.AddMinutes(-2)), new(20, 600, Now)]);
        var oldFc = GilBalanceCalculator.ForFreeCompany(10, snapshot);
        var currentFc = GilBalanceCalculator.ForFreeCompany(20, snapshot);

        Assert.Null(oldFc.RecordedGil);
        Assert.Equal(2, currentFc.Characters.Count);
        Assert.Equal(90, currentFc.WalletGil);
        Assert.Equal(600, currentFc.ChestGil);
        Assert.Equal(690, currentFc.RecordedGil);
        Assert.Equal(Now.AddMinutes(-1), currentFc.OldestWalletObservationAtUtc);
    }

    [Fact]
    public void MissingBalancesAreDifferentFromObservedZero()
    {
        var unknown = GilBalanceCalculator.ForFreeCompany(10, GilBalanceSnapshot.Empty);
        var zero = GilBalanceCalculator.ForFreeCompany(10, new([Wallet(1, 10, 0)], [new(10, 0, Now)]));
        Assert.False(unknown.HasAnyObservation);
        Assert.Null(unknown.WalletGil);
        Assert.Null(unknown.ChestGil);
        Assert.Null(unknown.RecordedGil);
        Assert.True(zero.HasAnyObservation);
        Assert.Equal(0, zero.WalletGil);
        Assert.Equal(0, zero.ChestGil);
        Assert.Equal(0, zero.RecordedGil);
    }

    [Fact]
    public void UnknownFcDoesNotMatchEveryUnassignedCharacter()
    {
        var snapshot = new GilBalanceSnapshot([Wallet(1, null, 50), Wallet(2, 0, 20), Wallet(3, 99, 80)], []);
        Assert.False(GilBalanceCalculator.ForFreeCompany(null, snapshot).HasAnyObservation);
        Assert.False(GilBalanceCalculator.ForFreeCompany(0, snapshot).HasAnyObservation);
        Assert.Equal(new ulong[] { 1, 2 }, GilBalanceCalculator.UnassignedCharacters(snapshot).Select(character => character.CharacterId));
    }

    [Fact]
    public void InvalidRecordsAreExcludedAndLargeWalletTotalsUseLong()
    {
        var snapshot = new GilBalanceSnapshot(
            [Wallet(0, 10, 1), Wallet(1, 10, -1), Wallet(2, 10, 999_999_999), Wallet(3, 10, 999_999_999), Wallet(4, 10, 999_999_999)],
            [new(0, 50, Now), new(10, -1, Now)]);
        var fc = GilBalanceCalculator.ForFreeCompany(10, snapshot);
        Assert.Equal(2_999_999_997L, fc.WalletGil);
        Assert.Null(fc.ChestGil);
    }

    private static CharacterGilObservation Wallet(ulong id, ulong? fc, long gil, int minute = 0)
        => new(id, fc, $"Character {id}", "Cerberus", gil, Now.AddMinutes(minute), false);
}
