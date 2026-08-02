using SubmarineEtaPlanner.Planner;
using SubmarineEtaPlanner.TrackerData;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class EtaPlannerIncrementalTests
{
    [Fact]
    public void IncrementalRefreshOnlyCalculatesChangedFc()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(100);
        var reader = new MutableStateReader([
            CreateFc(1, 50, now.AddDays(1)),
            CreateFc(2, 60, now.AddDays(1)),
        ]);
        var simulator = new RecordingSimulator();
        var service = new EtaPlannerService(reader, simulator);
        var settings = Settings();
        var first = service.Calculate(settings, now);
        simulator.Calls.Clear();
        reader.FreeCompanies = [
            CreateFc(1, 50, now.AddDays(1)),
            CreateFc(2, 61, now.AddDays(1)),
        ];

        var refreshed = service.Calculate(
            settings,
            now.AddMinutes(5),
            CancellationToken.None,
            null,
            first,
            ForecastRefreshMode.Incremental);

        Assert.Equal(["02"], simulator.Calls);
        Assert.Equal(1, refreshed.Metrics!.CalculatedFreeCompanies);
        Assert.Equal(1, refreshed.Metrics.ReusedFreeCompanies);
        Assert.Equal(FcCalculationStatus.Reused, Progress(refreshed, "01").Status);
        Assert.Equal(FcCalculationStatus.Complete, Progress(refreshed, "02").Status);
        Assert.Equal(first.Results.Single(result => Convert.ToHexString(result.FcId) == "01").GeneratedAtUtc,
            refreshed.Results.Single(result => Convert.ToHexString(result.FcId) == "01").GeneratedAtUtc);
    }

    [Fact]
    public void FullRefreshAlwaysCalculatesEveryFc()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(100);
        var reader = new MutableStateReader([CreateFc(1, 50, now.AddDays(1)), CreateFc(2, 60, now.AddDays(1))]);
        var simulator = new RecordingSimulator();
        var service = new EtaPlannerService(reader, simulator);
        var settings = Settings();
        var first = service.Calculate(settings, now);
        simulator.Calls.Clear();

        var refreshed = service.Calculate(
            settings,
            now.AddMinutes(5),
            CancellationToken.None,
            null,
            first,
            ForecastRefreshMode.Full);

        Assert.Equal(2, simulator.Calls.Count);
        Assert.Equal(0, refreshed.Metrics!.ReusedFreeCompanies);
    }

    [Fact]
    public void UnchangedIdleLevelingFcIsRecalculated()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(100);
        var reader = new MutableStateReader([CreateFc(1, 50, now.AddHours(-1), [])]);
        var simulator = new RecordingSimulator();
        var service = new EtaPlannerService(reader, simulator);
        var settings = Settings();
        var first = service.Calculate(settings, now);
        simulator.Calls.Clear();

        var refreshed = service.Calculate(
            settings,
            now.AddMinutes(5),
            CancellationToken.None,
            null,
            first,
            ForecastRefreshMode.Incremental);

        Assert.Equal(["01"], simulator.Calls);
        Assert.Equal(FcCalculationStatus.Complete, Assert.Single(refreshed.FcProgress).Status);
    }

    [Fact]
    public void ReturnedVoyageWithUnchangedTrackerDataKeepsPriorForecastAndWaits()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(100);
        var reader = new MutableStateReader([CreateFc(1, 50, now.AddHours(1), [1])]);
        var simulator = new RecordingSimulator();
        var service = new EtaPlannerService(reader, simulator);
        var settings = Settings();
        var first = service.Calculate(settings, now);
        simulator.Calls.Clear();

        var refreshed = service.Calculate(
            settings,
            now.AddHours(2),
            CancellationToken.None,
            null,
            first,
            ForecastRefreshMode.Incremental);

        Assert.Empty(simulator.Calls);
        Assert.Equal(FcCalculationStatus.AwaitingTrackerUpdate, Assert.Single(refreshed.FcProgress).Status);
        Assert.Equal(1, refreshed.Metrics!.AwaitingTrackerFreeCompanies);
        Assert.False(refreshed.IsComplete);
        Assert.Equal(first.Results[0], refreshed.Results[0]);

        var repeated = service.Calculate(
            settings,
            now.AddHours(3),
            CancellationToken.None,
            null,
            refreshed,
            ForecastRefreshMode.Incremental);
        Assert.Empty(simulator.Calls);
        Assert.Equal(FcCalculationStatus.AwaitingTrackerUpdate, Assert.Single(repeated.FcProgress).Status);
    }

    [Fact]
    public void SettingsFingerprintMismatchDisablesReuse()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(100);
        var reader = new MutableStateReader([CreateFc(1, 50, now.AddDays(1))]);
        var simulator = new RecordingSimulator();
        var service = new EtaPlannerService(reader, simulator);
        var settings = Settings();
        var first = service.Calculate(settings, now);
        simulator.Calls.Clear();
        settings.UnlockSuccessProbability = 0.5;

        var refreshed = service.Calculate(
            settings,
            now.AddMinutes(5),
            CancellationToken.None,
            null,
            first,
            ForecastRefreshMode.Incremental);

        Assert.Equal(["01"], simulator.Calls);
        Assert.Equal(0, refreshed.Metrics!.ReusedFreeCompanies);
    }

    [Fact]
    public void PartialResultIsRetriedEvenWhenFcDataIsUnchanged()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(100);
        var reader = new MutableStateReader([CreateFc(1, 50, now.AddDays(1))]);
        var simulator = new RecordingSimulator { ReturnPartial = true };
        var service = new EtaPlannerService(reader, simulator);
        var settings = Settings();
        var first = service.Calculate(settings, now);
        simulator.Calls.Clear();
        simulator.ReturnPartial = false;

        var refreshed = service.Calculate(
            settings,
            now.AddMinutes(5),
            CancellationToken.None,
            null,
            first,
            ForecastRefreshMode.Incremental);

        Assert.Equal(["01"], simulator.Calls);
        Assert.True(Assert.Single(refreshed.Results).IsComplete);
    }

    [Fact]
    public void IncrementalRefreshAddsAndRemovesFcs()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(100);
        var reader = new MutableStateReader([CreateFc(1, 50, now.AddDays(1)), CreateFc(2, 60, now.AddDays(1))]);
        var simulator = new RecordingSimulator();
        var service = new EtaPlannerService(reader, simulator);
        var settings = Settings();
        var first = service.Calculate(settings, now);
        simulator.Calls.Clear();
        reader.FreeCompanies = [CreateFc(2, 60, now.AddDays(1)), CreateFc(3, 70, now.AddDays(1))];

        var refreshed = service.Calculate(
            settings,
            now.AddMinutes(5),
            CancellationToken.None,
            null,
            first,
            ForecastRefreshMode.Incremental);

        Assert.Equal(["03"], simulator.Calls);
        Assert.Equal(["02", "03"], refreshed.FreeCompanies.Select(fc => fc.FcIdKey).Order().ToArray());
        Assert.Equal(["02", "03"], refreshed.Results.Select(result => Convert.ToHexString(result.FcId)).Order().ToArray());
    }

    private static FcCalculationProgress Progress(EtaPlannerSnapshot snapshot, string key)
        => snapshot.FcProgress.Single(progress => progress.FcIdKey == key);

    private static EtaSettings Settings()
        => EtaSettings.CreateDefault() with { TargetRank = 100, CalculationTimeLimitSeconds = 0 };

    private static FcState CreateFc(byte id, int rank, DateTimeOffset returnAt, IReadOnlyList<uint>? route = null)
    {
        var fcId = new[] { id };
        var submarine = new SubmarineState(
            fcId,
            id,
            $"Sub {id}",
            rank,
            0,
            100,
            SubmarineBuildParts.Empty,
            returnAt,
            route ?? [1],
            true,
            []);
        var state = new FcState(fcId, $"FC{id}", "World", new HashSet<uint> { 1 }, new HashSet<uint> { 1 }, [submarine]);
        return state with { DataFingerprint = FcDataFingerprint.Create(state) };
    }

    private sealed class MutableStateReader(IReadOnlyList<FcState> freeCompanies) : ISubmarineTrackerStateReader
    {
        public IReadOnlyList<FcState> FreeCompanies { get; set; } = freeCompanies;

        public SubmarineTrackerDataFingerprint GetDataFingerprint(EtaSettings settings)
            => SubmarineTrackerDataFingerprint.Capture("test.db");

        public IReadOnlyList<FcState> Read(EtaSettings settings, ICollection<string> warnings) => FreeCompanies;
    }

    private sealed class RecordingSimulator : IEtaSimulator
    {
        public List<string> Calls { get; } = [];

        public bool ReturnPartial { get; set; }

        public EtaResult Simulate(
            FcState fc,
            EtaSettings settings,
            DateTimeOffset now,
            DateTimeOffset? deadlineUtc,
            CancellationToken cancellationToken)
        {
            Calls.Add(fc.FcIdKey);
            var subResults = fc.Submarines.Select(submarine => new PerSubEtaResult(
                submarine.SubmarineId,
                submarine.Name,
                submarine.Rank,
                settings.TargetRank,
                now.AddDays(1),
                TimeSpan.FromDays(1),
                1,
                "SSUW",
                [1],
                [],
                [],
                [],
                CalculationStatus.Complete,
                null)).ToArray();
            return new EtaResult(
                fc.FcId,
                fc.DisplayName,
                now,
                settings.TargetRank,
                settings.SimulationMode,
                subResults,
                now.AddDays(1),
                1,
                [],
                [],
                [],
                ReturnPartial ? CalculationStatus.Partial : CalculationStatus.Complete,
                ReturnPartial ? "Fixture partial result." : null);
        }
    }
}
