using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class SubmarineRoleTests
{
    [Theory]
    [InlineData(SubmarineAssignment.Auto, 89, EffectiveSubmarineRole.Leveling)]
    [InlineData(SubmarineAssignment.Auto, 90, EffectiveSubmarineRole.Farming)]
    [InlineData(SubmarineAssignment.Auto, 91, EffectiveSubmarineRole.Farming)]
    [InlineData(SubmarineAssignment.Leveling, 91, EffectiveSubmarineRole.Leveling)]
    [InlineData(SubmarineAssignment.Farming, 89, EffectiveSubmarineRole.Farming)]
    [InlineData(SubmarineAssignment.Paused, 89, EffectiveSubmarineRole.Paused)]
    public void ResolverHonorsExplicitAssignments(
        SubmarineAssignment assignment,
        int rank,
        EffectiveSubmarineRole expected)
    {
        Assert.Equal(expected, SubmarineRoleResolver.Resolve(assignment, rank, effectiveTargetRank: 90));
    }

    [Fact]
    public void MixedRoleSummaryDrivesBothRoleFilters()
    {
        var projection = CreateProjection(
            [50, 90, 70],
            new Dictionary<long, SubmarineAssignment>
            {
                [1] = SubmarineAssignment.Leveling,
                [2] = SubmarineAssignment.Farming,
                [3] = SubmarineAssignment.Paused,
            });

        Assert.Equal(new FcRoleSummary(1, 1, 1), projection.RoleSummary);
        Assert.True(FleetPresentationFiltering.Includes(projection, FleetMode.Leveling));
        Assert.True(FleetPresentationFiltering.Includes(projection, FleetMode.Farming));
        Assert.True(FleetPresentationFiltering.Includes(projection, null));
    }

    [Fact]
    public void AllAutoRolesRetainRankBasedBehavior()
    {
        var leveling = CreateProjection([89, 90]);
        var farming = CreateProjection([90, 91]);

        Assert.Equal(new FcRoleSummary(1, 1, 0), leveling.RoleSummary);
        Assert.Equal(
            [EffectiveSubmarineRole.Leveling, EffectiveSubmarineRole.Farming],
            leveling.Submarines.OrderBy(submarine => submarine.SubmarineId).Select(submarine => submarine.EffectiveRole).ToArray());
        Assert.Equal(FleetMode.Leveling, leveling.Mode);
        Assert.Equal(new FcRoleSummary(0, 2, 0), farming.RoleSummary);
        Assert.Equal(FleetMode.Farming, farming.Mode);
    }

    [Fact]
    public void ExplicitLevelingAboveTargetRemainsLevelingAndTargetComplete()
    {
        var submarine = Assert.Single(CreateProjection(
            [91],
            new Dictionary<long, SubmarineAssignment> { [1] = SubmarineAssignment.Leveling }).Submarines);

        Assert.Equal(EffectiveSubmarineRole.Leveling, submarine.EffectiveRole);
        Assert.True(submarine.IsTargetComplete);
        Assert.Equal(RecommendedAction.SendLevelingRouteNow, submarine.Action);
    }

    [Fact]
    public void ExplicitFarmingBelowTargetRemainsFarming()
    {
        var submarine = Assert.Single(CreateProjection(
            [89],
            new Dictionary<long, SubmarineAssignment> { [1] = SubmarineAssignment.Farming }).Submarines);

        Assert.Equal(EffectiveSubmarineRole.Farming, submarine.EffectiveRole);
        Assert.False(submarine.IsTargetComplete);
        Assert.Equal(RecommendedAction.ChooseFarmingRoute, submarine.Action);
    }

    [Fact]
    public void PausedSubmarinesAreCountedSeparately()
    {
        var projection = CreateProjection(
            [50, 90],
            new Dictionary<long, SubmarineAssignment>
            {
                [1] = SubmarineAssignment.Paused,
                [2] = SubmarineAssignment.Paused,
            });

        Assert.Equal(new FcRoleSummary(0, 0, 2), projection.RoleSummary);
        Assert.All(projection.Submarines, submarine =>
        {
            Assert.Equal(RecommendedAction.Paused, submarine.Action);
        });
        Assert.False(FleetPresentationFiltering.Includes(projection, FleetMode.Leveling));
        Assert.False(FleetPresentationFiltering.Includes(projection, FleetMode.Farming));
        Assert.True(FleetPresentationFiltering.Includes(projection, null));
    }

    [Theory]
    [InlineData(1, 3, 0, "3 farming · 1 leveling")]
    [InlineData(0, 2, 1, "2 farming · 1 paused")]
    [InlineData(0, 4, 0, "4 farming")]
    [InlineData(2, 0, 1, "2 leveling · 1 paused")]
    public void RoleSummaryFormattingUsesDeterministicOrder(
        int leveling,
        int farming,
        int paused,
        string expected)
    {
        Assert.Equal(expected, FcRoleSummaryFormatter.Format(new FcRoleSummary(leveling, farming, paused)));
    }

    private static FcOperationalProjection CreateProjection(
        IReadOnlyList<int> ranks,
        IReadOnlyDictionary<long, SubmarineAssignment>? assignments = null)
    {
        byte[] fcId = [1];
        var submarines = ranks.Select((rank, index) => new SubmarineState(
            fcId,
            index + 1,
            $"Sub {index + 1}",
            rank,
            0,
            100,
            SubmarineBuildParts.Empty,
            DateTimeOffset.MinValue,
            [],
            true,
            [])).ToArray();
        var fc = new FcState(fcId, "TEST", "World", new HashSet<uint>(), new HashSet<uint>(), submarines);

        return FleetPresentationBuilder.Create(
            fc,
            null,
            EtaSettings.CreateDefault() with { TargetRank = 90 },
            new StubCatalog(),
            DateTimeOffset.UnixEpoch,
            assignments);
    }

    private sealed class StubCatalog : ISubmarineCatalog
    {
        public int MaximumRank => 120;
        public IReadOnlyList<UnlockRule> UnlockRules => [];
        public SubmarineBuild ResolveBuild(string buildCode, int rank) => throw new NotSupportedException();
        public SubmarineBuild? ResolveBuild(SubmarineBuildParts buildParts, int rank) => null;
        public RouteSearchResult FindBestRoute(RouteSearchRequest request) => throw new NotSupportedException();
        public uint CalculateExp(IReadOnlyList<uint> route, SubmarineBuild build, ExpMode expMode) => throw new NotSupportedException();
        public TimeSpan CalculateDuration(IReadOnlyList<uint> route, SubmarineBuild build) => throw new NotSupportedException();
        public (int Rank, uint CurrentExp, uint NextLevelExp) ApplyExp(int rank, uint currentExp, uint gainedExp, int targetRank)
            => throw new NotSupportedException();
        public string PointName(uint point) => point.ToString();
        public int GetPointRequiredRank(uint point) => 1;
    }
}
