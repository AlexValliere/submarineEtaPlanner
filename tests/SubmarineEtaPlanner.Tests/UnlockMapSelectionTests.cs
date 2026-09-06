using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class UnlockMapSelectionTests
{
    [Fact]
    public void SearchReturnsMapQualifiedMatchesAndSelectedCrossMapPath()
    {
        var map = Presentation();
        var results = UnlockMapSelection.Search(map, "a");
        Assert.Equal(2, results.Count);
        Assert.Equal(new uint[] { 1, 2 }, results.Select(item => item.Destination.MapId));
        Assert.Equal(new uint[] { 1, 2, 3 }, UnlockMapSelection.Path(map, 3).Order());
        Assert.Single(UnlockMapSelection.Search(map, "  deep sector  "));
    }

    [Fact]
    public void RemainingFilterRetainsCompletedPrerequisitesAndExplicitSelection()
    {
        var map = Presentation();
        Assert.Equal(new uint[] { 1, 2, 3 }, UnlockMapSelection.Visible(map, true, null).Order());
        Assert.Equal(new uint[] { 1, 2, 3, 4 }, UnlockMapSelection.Visible(map, true, 4).Order());
        Assert.Equal(4, UnlockMapSelection.Visible(map, false, null).Count);
        Assert.Equal(4, UnlockMapSelection.Visible(map with { UnlockDataKnown = false }, true, null).Count);
        Assert.Empty(UnlockMapSelection.Path(map, 99));
    }

    [Fact]
    public void CyclicIncomingRulesCannotHangMapSelection()
    {
        var map = Presentation();
        var first = map.Maps[0];
        map = map with { Maps = [first with { Destinations = first.Destinations.Select(item => item.Destination.SectorId == 1
            ? item with { IncomingRule = new(2, 1, 1, 1) } : item).ToArray() }, map.Maps[1]] };
        Assert.Equal(new uint[] { 1, 2, 3 }, UnlockMapSelection.Path(map, 3).Order());
    }

    private static FcUnlockMapsPresentation Presentation()
    {
        UnlockMapDestinationPresentation Destination(uint id, uint map, string code, string name, UnlockDestinationState state,
            uint? source = null, uint[]? path = null) => new(new(id, code, name, map, $"Map {map}", 1), state,
                source is { } parent ? new UnlockRule(parent, id, 1, 1) : null, path ?? [], []);
        var first = new UnlockMapPresentation(1, "Map 1", [
            Destination(1, 1, "A", "Origin", UnlockDestinationState.Explored),
            Destination(2, 1, "B", "Source", UnlockDestinationState.Unlocked, 1),
            Destination(4, 1, "Z", "Completed", UnlockDestinationState.Explored)], [], 3, 3, 2, 0);
        var second = new UnlockMapPresentation(2, "Map 2", [
            Destination(3, 2, "A", "Deep sector", UnlockDestinationState.Locked, 2, [2, 3])], [], 1, 0, 0, 1);
        return new("fc", "FC", true, [first, second], 4, 3, 2, 1);
    }
}
