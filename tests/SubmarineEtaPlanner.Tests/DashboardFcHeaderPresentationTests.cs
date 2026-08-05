using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class DashboardFcHeaderPresentationTests
{
    [Fact]
    public void UnderwayVoyageUsesCompactAlignedHeaderValues()
    {
        var fc = CreateFc("TEST", "Cerberus", rank: 113, salvageGil: 12_000_000);
        var primary = new CurrentVoyageProgressPresentation(
            1,
            "Nautilus",
            CurrentVoyageProgressState.Underway,
            DateTimeOffset.UnixEpoch.AddHours(4),
            DateTimeOffset.UnixEpoch,
            TimeSpan.FromHours(4),
            0.5f,
            "2h 0m",
            null);
        var voyages = new FcCurrentVoyageProgressPresentation(primary, [primary], 0);

        var presentation = DashboardFcHeaderPresentation.Create(fc, "Median 4d 2h", voyages);

        Assert.Equal("TEST", presentation.FreeCompanyTag);
        Assert.Equal("Cerberus", presentation.World);
        Assert.Equal("Median 4d 2h", presentation.TargetEta);
        Assert.Equal("12m gil", presentation.Salvage);
        Assert.Equal("In 2h 0m", presentation.CurrentVoyage);
    }

    [Fact]
    public void ReadyAndIdleVoyagesRemainUnambiguous()
    {
        var fc = CreateFc("TEST", string.Empty, rank: 114, salvageGil: 0);
        var ready = new CurrentVoyageProgressPresentation(
            1,
            "Nautilus",
            CurrentVoyageProgressState.ReadyToCollect,
            DateTimeOffset.UnixEpoch,
            null,
            null,
            1f,
            "Ready to collect",
            null);

        var readyPresentation = DashboardFcHeaderPresentation.Create(
            fc,
            "Ready",
            new FcCurrentVoyageProgressPresentation(ready, [ready], 3));
        var idlePresentation = DashboardFcHeaderPresentation.Create(
            fc,
            "Ready",
            new FcCurrentVoyageProgressPresentation(null, [], 0));

        Assert.Equal("—", readyPresentation.World);
        Assert.Equal("3 ready to collect", readyPresentation.CurrentVoyage);
        Assert.Equal("—", idlePresentation.CurrentVoyage);
    }

    [Fact]
    public void FcReadinessRequiresEveryTrackedSubmarineToReachTarget()
    {
        var ready = CreateFc("READY", "Omega", rank: 114, salvageGil: 0);
        var mixed = ready with
        {
            Submarines =
            [
                CreateSubmarine(1, 114),
                CreateSubmarine(2, 113),
            ],
        };
        var empty = ready with { Submarines = [] };

        Assert.True(ResultsViewState.IsReady(ready, 114));
        Assert.False(ResultsViewState.IsReady(mixed, 114));
        Assert.False(ResultsViewState.IsReady(empty, 114));
    }

    private static FcState CreateFc(string tag, string world, int rank, long salvageGil)
    {
        var submarine = CreateSubmarine(1, rank) with
        {
            Salvage = salvageGil == 0
                ? SubmarineSalvageSummary.Empty
                : new SubmarineSalvageSummary(
                    1,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch,
                    [new SalvageItemTotal(1, "Salvage", 1, salvageGil)]),
        };
        return new FcState([1], tag, world, new HashSet<uint>(), new HashSet<uint>(), [submarine]);
    }

    private static SubmarineState CreateSubmarine(long id, int rank)
        => new(
            [1],
            id,
            $"Submarine {id}",
            rank,
            0,
            0,
            SubmarineBuildParts.Empty,
            DateTimeOffset.UnixEpoch,
            [],
            false,
            []);
}
