using SubmarineEtaPlanner.Planner;
using SubmarineEtaPlanner.SubmarineTrackerCompat;
using SubmarineEtaPlanner.TrackerData;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class EtaPlannerProgressTests
{
    [Fact]
    public void SequentialCalculationPublishesEveryFcBeforeResultsAreReady()
    {
        var catalog = new CompatSubmarineCatalog();
        var unlockGraph = new RouteUnlockGraph(catalog);
        var simulator = new EtaSimulator(
            new BuildResolver(catalog),
            unlockGraph,
            new RouteSelector(catalog, unlockGraph),
            catalog);
        var service = new EtaPlannerService(new StubStateReader(
        [
            CreateFc([2], "BETA", 2),
            CreateFc([1], "ALPHA", 1),
        ]), simulator, dataDiagnostics: new StubDataDiagnostics(["Catalog compatibility notice."]));
        var settings = EtaSettings.CreateDefault() with
        {
            TargetRank = 1,
            UnlockSuccessProbability = 1.0,
        };
        var snapshots = new List<EtaPlannerSnapshot>();

        var result = service.Calculate(
            settings,
            DateTimeOffset.UnixEpoch,
            CancellationToken.None,
            snapshots.Add);

        var initial = snapshots[0];
        Assert.True(initial.IsRunning);
        Assert.Equal(2, initial.FreeCompanies.Count);
        Assert.Empty(initial.Results);
        Assert.All(initial.FcProgress, progress => Assert.Equal(FcCalculationStatus.Queued, progress.Status));
        Assert.Contains("Catalog compatibility notice.", initial.Warnings);
        Assert.All(snapshots, snapshot => Assert.InRange(
            snapshot.FcProgress.Count(progress => progress.Status == FcCalculationStatus.Calculating),
            0,
            1));
        Assert.Contains(snapshots, snapshot => snapshot.IsRunning && snapshot.Results.Count == 1);
        Assert.False(result.IsRunning);
        Assert.True(result.IsComplete);
        Assert.Equal(2, result.Results.Count);
        Assert.All(result.FcProgress, progress => Assert.Equal(FcCalculationStatus.Complete, progress.Status));
    }

    private static FcState CreateFc(byte[] fcId, string tag, long submarineId)
    {
        var submarine = new SubmarineState(
            fcId,
            submarineId,
            $"Sub {submarineId}",
            1,
            100,
            0,
            SubmarineBuildParts.Empty,
            DateTimeOffset.UnixEpoch,
            [],
            true,
            []);
        return new FcState(fcId, tag, "World", new HashSet<uint> { 1 }, new HashSet<uint> { 1 }, [submarine]);
    }

    private sealed class StubStateReader(IReadOnlyList<FcState> freeCompanies) : ISubmarineTrackerStateReader
    {
        public SubmarineTrackerDataFingerprint GetDataFingerprint(EtaSettings settings)
            => SubmarineTrackerDataFingerprint.Capture("test.db");

        public IReadOnlyList<FcState> Read(EtaSettings settings, ICollection<string> warnings) => freeCompanies;
    }

    private sealed class StubDataDiagnostics(IReadOnlyList<string> warnings) : IPlannerDataDiagnostics
    {
        public IReadOnlyList<string> GetPlannerDataWarnings() => warnings;
    }
}
