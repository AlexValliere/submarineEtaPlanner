using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class CurrentVoyageProgressFormatterTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(100);

    [Fact]
    public void KnownVoyageCalculatesElapsedFractionFromRouteAndBuild()
    {
        var catalog = new TestCatalog(TimeSpan.FromDays(2));
        var submarine = CreateSubmarine(
            "Nautilus",
            Now.AddDays(1),
            route: [1],
            currentVoyageKnown: true);

        var progress = CurrentVoyageProgressFormatter.Create(submarine, catalog, Now);

        Assert.Equal(CurrentVoyageProgressState.Underway, progress.State);
        Assert.Equal(0.5f, progress.Fraction);
        Assert.Equal(Now.AddDays(-1), progress.DepartedAtUtc);
        Assert.Equal(TimeSpan.FromDays(2), progress.Duration);
        Assert.Equal("1d 0h 0m", progress.Countdown);
    }

    [Fact]
    public void ProgressIsClampedWhenInferredDepartureIsInTheFuture()
    {
        var catalog = new TestCatalog(TimeSpan.FromDays(2));
        var submarine = CreateSubmarine("Nautilus", Now.AddDays(3), [1], currentVoyageKnown: true);

        var progress = CurrentVoyageProgressFormatter.Create(submarine, catalog, Now);

        Assert.Equal(0f, progress.Fraction);
        Assert.Contains("clamped", progress.ProgressUnavailableReason);
    }

    [Fact]
    public void ReturnedKnownVoyageIsReadyEvenWhenDurationCannotBeCalculated()
    {
        var catalog = new TestCatalog(TimeSpan.Zero);
        var submarine = CreateSubmarine("Nautilus", Now, [1], currentVoyageKnown: true);

        var progress = CurrentVoyageProgressFormatter.Create(submarine, catalog, Now);

        Assert.Equal(CurrentVoyageProgressState.ReadyToCollect, progress.State);
        Assert.Equal(1f, progress.Fraction);
        Assert.Equal("Ready to collect", progress.Countdown);
    }

    [Fact]
    public void UnknownFutureVoyageKeepsCountdownWithoutPercentage()
    {
        var submarine = CreateSubmarine(
            "Nautilus",
            Now.AddHours(6),
            route: [],
            currentVoyageKnown: false);

        var progress = CurrentVoyageProgressFormatter.Create(submarine, new TestCatalog(), Now);

        Assert.Equal(CurrentVoyageProgressState.Syncing, progress.State);
        Assert.Null(progress.Fraction);
        Assert.Equal("6h 0m", progress.Countdown);
        Assert.Contains("not synced", progress.ProgressUnavailableReason);
    }

    [Fact]
    public void UnknownVoyageBecomesReadyWhenItsCountdownExpires()
    {
        var submarine = CreateSubmarine(
            "Nautilus",
            Now.AddSeconds(-1),
            route: [],
            currentVoyageKnown: false);

        var progress = CurrentVoyageProgressFormatter.Create(submarine, new TestCatalog(), Now);

        Assert.Equal(CurrentVoyageProgressState.ReadyToCollect, progress.State);
        Assert.Equal(1f, progress.Fraction);
        Assert.Equal("Ready to collect", progress.Countdown);
    }

    [Fact]
    public void MissingBuildKeepsKnownVoyageCountdownWithoutPercentage()
    {
        var submarine = CreateSubmarine(
            "Nautilus",
            Now.AddMinutes(30),
            route: [1],
            currentVoyageKnown: true) with
        {
            BuildParts = SubmarineBuildParts.Empty,
        };

        var progress = CurrentVoyageProgressFormatter.Create(submarine, new TestCatalog(), Now);

        Assert.Equal(CurrentVoyageProgressState.Underway, progress.State);
        Assert.Null(progress.Fraction);
        Assert.Equal("30m 0s", progress.Countdown);
        Assert.Contains("build", progress.ProgressUnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyPastVoyageIsIdle()
    {
        var submarine = CreateSubmarine("Nautilus", Now.AddHours(-1), [], currentVoyageKnown: true);

        var progress = CurrentVoyageProgressFormatter.Create(submarine, new TestCatalog(), Now);

        Assert.Equal(CurrentVoyageProgressState.Idle, progress.State);
        Assert.False(progress.IsActive);
        Assert.Equal("—", progress.Countdown);
    }

    [Fact]
    public void RecordedRankDoesNotHideAnActiveFarmingVoyage()
    {
        var submarine = CreateSubmarine(
            "Nautilus",
            Now.AddHours(4),
            route: [1],
            currentVoyageKnown: true,
            rank: 120);

        var progress = CurrentVoyageProgressFormatter.Create(submarine, new TestCatalog(), Now);

        Assert.Equal(CurrentVoyageProgressState.Underway, progress.State);
        Assert.True(progress.IsActive);
    }

    [Fact]
    public void FcSummaryPrioritizesReadyVoyagesOverFutureReturns()
    {
        var submarines = new[]
        {
            CreateSubmarine("Future", Now.AddMinutes(5), [1], currentVoyageKnown: true, id: 1),
            CreateSubmarine("Ready B", Now.AddMinutes(-1), [1], currentVoyageKnown: true, id: 2),
            CreateSubmarine("Ready A", Now.AddMinutes(-2), [1], currentVoyageKnown: true, id: 3),
        };

        var summary = CurrentVoyageProgressFormatter.CreateForFc(submarines, new TestCatalog(), Now);

        Assert.Equal(2, summary.ReadyCount);
        Assert.Equal("2 ready to collect", summary.HeaderLabel);
        Assert.Equal("Ready A", summary.Primary?.SubmarineName);
    }

    [Fact]
    public void FcSummaryChoosesEarliestReturnThenNameDeterministically()
    {
        var returnAt = Now.AddHours(4);
        var submarines = new[]
        {
            CreateSubmarine("Zulu", returnAt, [1], currentVoyageKnown: true, id: 1),
            CreateSubmarine("Alpha", returnAt, [1], currentVoyageKnown: true, id: 2),
            CreateSubmarine("Later", returnAt.AddMinutes(1), [1], currentVoyageKnown: true, id: 3),
        };

        var summary = CurrentVoyageProgressFormatter.CreateForFc(submarines, new TestCatalog(), Now);

        Assert.Equal("Alpha", summary.Primary?.SubmarineName);
        Assert.Equal("Next return in 4h 0m", summary.HeaderLabel);
    }

    [Theory]
    [InlineData(1, 14, 32, 0, "1d 14h 32m")]
    [InlineData(0, 14, 32, 0, "14h 32m")]
    [InlineData(0, 0, 32, 18, "32m 18s")]
    [InlineData(0, 0, 0, 42, "42s")]
    [InlineData(0, 0, 0, 0, "Ready to collect")]
    public void CountdownUsesCompactExplicitUnits(int days, int hours, int minutes, int seconds, string expected)
    {
        var remaining = new TimeSpan(days, hours, minutes, seconds);

        Assert.Equal(expected, CurrentVoyageProgressFormatter.FormatCountdown(remaining));
    }

    private static SubmarineState CreateSubmarine(
        string name,
        DateTimeOffset returnAtUtc,
        IReadOnlyList<uint> route,
        bool currentVoyageKnown,
        long id = 1,
        int rank = 114)
        => new(
            [1],
            id,
            name,
            rank,
            0,
            1_000,
            new SubmarineBuildParts(1, 2, 3, 4),
            returnAtUtc,
            route,
            currentVoyageKnown,
            []);

    private sealed class TestCatalog(TimeSpan? duration = null) : ISubmarineCatalog
    {
        public int MaximumRank => 120;

        public IReadOnlyList<UnlockRule> UnlockRules => [];

        public SubmarineBuild ResolveBuild(string buildCode, int rank)
            => new(buildCode, rank, 0, 0, 0, 999, 100);

        public SubmarineBuild? ResolveBuild(SubmarineBuildParts buildParts, int rank)
            => buildParts == SubmarineBuildParts.Empty ? null : ResolveBuild("TEST", rank);

        public RouteSearchResult FindBestRoute(RouteSearchRequest request)
            => new(null, 0, false);

        public uint CalculateExp(IReadOnlyList<uint> route, SubmarineBuild build, ExpMode expMode)
            => 0;

        public TimeSpan CalculateDuration(IReadOnlyList<uint> route, SubmarineBuild build)
            => duration ?? TimeSpan.FromHours(8);

        public (int Rank, uint CurrentExp, uint NextLevelExp) ApplyExp(
            int rank,
            uint currentExp,
            uint gainedExp,
            int targetRank)
            => (rank, currentExp, 1_000);

        public string PointName(uint point) => point.ToString();

        public int GetPointRequiredRank(uint point) => 1;
    }
}
