using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class VoyageProgressFormatterTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(100);

    [Fact]
    public void TauchbootThreeSequenceKeepsCurrentVoyageInTheTotalUntilCollection()
    {
        var idle = CreateResult(voyageCount: 2);
        var underway = CreateResult(
            voyageCount: 0,
            currentRoute: [1, 2, 3, 4],
            currentReturnAtUtc: Now.AddDays(2));
        var collected = CreateResult(voyageCount: 0, startingRank: 85, finalRank: 85);

        var beforeSending = VoyageProgressFormatter.Create(idle, targetRank: 85, Now);
        var afterSending = VoyageProgressFormatter.Create(underway, targetRank: 85, Now);
        var awaitingCollection = VoyageProgressFormatter.Create(underway, targetRank: 85, Now.AddDays(3));
        var afterCollection = VoyageProgressFormatter.Create(collected, targetRank: 85, Now.AddDays(3));

        Assert.Equal("2", beforeSending.Label);
        Assert.Equal("1 · underway", afterSending.Label);
        Assert.Equal("1 · collect", awaitingCollection.Label);
        Assert.Equal("0", afterCollection.Label);
        Assert.Equal(1, afterSending.VoyagesLeft);
        Assert.Equal(afterSending.VoyagesLeft, awaitingCollection.VoyagesLeft);
    }

    [Fact]
    public void UnderwayVoyageAndFuturePlanAreBothIncluded()
    {
        var result = CreateResult(
            voyageCount: 1,
            currentRoute: [7],
            currentReturnAtUtc: Now.AddHours(6));

        var display = VoyageProgressFormatter.Create(result, targetRank: 85, Now);

        Assert.Equal("2 · underway", display.Label);
        Assert.Equal(2, display.VoyagesLeft);
        Assert.Contains("1 underway and 1 voyage planned after collection", display.Tooltip);
    }

    [Fact]
    public void CollectionWithInsufficientExpLeavesFuturePlanCount()
    {
        var result = CreateResult(voyageCount: 1);

        var display = VoyageProgressFormatter.Create(result, targetRank: 85, Now);

        Assert.Equal("1", display.Label);
        Assert.Equal(1, display.VoyagesLeft);
        Assert.Equal(VoyageProgressState.Planned, display.State);
    }

    [Fact]
    public void UnknownCurrentRouteDisplaysSyncingInsteadOfNumber()
    {
        var result = CreateResult(voyageCount: 1, currentVoyageUnknown: true);

        var display = VoyageProgressFormatter.Create(result, targetRank: 85, Now);

        Assert.Equal("— · syncing", display.Label);
        Assert.Null(display.VoyagesLeft);
        Assert.Equal(VoyageProgressState.Syncing, display.State);
    }

    [Fact]
    public void FarmingVoyageIsNotCountedAfterTargetRankIsRecorded()
    {
        var result = CreateResult(
            voyageCount: 0,
            startingRank: 85,
            finalRank: 85,
            currentRoute: [7],
            currentReturnAtUtc: Now.AddHours(6));

        var display = VoyageProgressFormatter.Create(result, targetRank: 85, Now);

        Assert.Equal("0", display.Label);
        Assert.Equal(0, display.VoyagesLeft);
        Assert.Equal(VoyageProgressState.TargetReached, display.State);
    }

    [Fact]
    public void PassiveSubmarineDoesNotExposeLevelingVoyagesLeft()
    {
        var result = CreateResult(
            voyageCount: 0,
            currentRoute: [7],
            currentReturnAtUtc: Now.AddHours(6)) with
        {
            IncludedInLevelingTarget = false,
        };

        var display = VoyageProgressFormatter.Create(result, targetRank: 85, Now);

        Assert.Equal("—", display.Label);
        Assert.Null(display.VoyagesLeft);
        Assert.Equal(VoyageProgressState.Planned, display.State);
        Assert.Contains("not included", display.Tooltip, StringComparison.OrdinalIgnoreCase);
    }

    private static PerSubEtaResult CreateResult(
        int voyageCount,
        int startingRank = 84,
        int finalRank = 85,
        IReadOnlyList<uint>? currentRoute = null,
        DateTimeOffset? currentReturnAtUtc = null,
        bool currentVoyageUnknown = false)
        => new(
            1,
            "Tauchboot-3",
            startingRank,
            finalRank,
            Now,
            TimeSpan.Zero,
            voyageCount,
            "SSUW",
            [],
            [],
            [],
            [],
            CalculationStatus.Complete,
            null)
        {
            CurrentRoute = currentRoute ?? [],
            CurrentReturnAtUtc = currentReturnAtUtc,
            CurrentVoyageUnknown = currentVoyageUnknown,
        };
}
