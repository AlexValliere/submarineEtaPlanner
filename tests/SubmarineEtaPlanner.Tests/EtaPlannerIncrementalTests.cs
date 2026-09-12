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
        var reader = new MutableStateReader([CreateFc(1, 50, now.AddDays(1)), CreateFc(0xab, 60, now.AddDays(1))]);
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
        var progress = Assert.Single(refreshed.FcProgress);
        Assert.Equal(FcCalculationStatus.AwaitingTrackerUpdate, progress.Status);
        Assert.Contains("Collect returned submarines", progress.Message);
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
    public void ChangingOneFcOverrideOnlyRecalculatesThatFc()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(100);
        var reader = new MutableStateReader([CreateFc(1, 50, now.AddDays(1)), CreateFc(2, 60, now.AddDays(1))]);
        var simulator = new RecordingSimulator();
        var service = new EtaPlannerService(reader, simulator, maximumRank: 120);
        var settings = Settings();
        var initialRequest = PlannerCalculationRequest.FromGlobalSettings(settings);
        var first = service.Calculate(initialRequest, now, CancellationToken.None);
        simulator.Calls.Clear();

        var changedRequest = new PlannerCalculationRequest(
            settings,
            new Dictionary<string, FcSimulationOverride>
            {
                ["02"] = new(TargetRank: 110),
            });
        var refreshed = service.Calculate(
            changedRequest,
            now.AddMinutes(5),
            CancellationToken.None,
            previousSnapshot: first,
            refreshMode: ForecastRefreshMode.Incremental);

        Assert.Equal(["02"], simulator.Calls);
        Assert.Equal(100, refreshed.Results.Single(result => Convert.ToHexString(result.FcId) == "01").TargetRank);
        Assert.Equal(110, refreshed.Results.Single(result => Convert.ToHexString(result.FcId) == "02").TargetRank);
        Assert.Equal(1, refreshed.Metrics!.ReusedFreeCompanies);
    }

    [Fact]
    public void AssignmentChangeInvalidatesPerFcCalculationFingerprint()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(100);
        var reader = new MutableStateReader([CreateFc(1, 50, now.AddDays(1))]);
        var simulator = new RecordingSimulator();
        var service = new EtaPlannerService(reader, simulator);
        var settings = Settings();
        var first = service.Calculate(
            PlannerCalculationRequest.FromGlobalSettings(settings),
            now,
            CancellationToken.None);
        simulator.Calls.Clear();
        var changedRequest = new PlannerCalculationRequest(
            settings,
            new Dictionary<string, FcSimulationOverride>
            {
                ["01"] = new()
                {
                    SubmarineAssignments = new Dictionary<long, SubmarineAssignment>
                    {
                        [1] = SubmarineAssignment.Farming,
                    },
                },
            });

        var refreshed = service.Calculate(
            changedRequest,
            now.AddMinutes(5),
            CancellationToken.None,
            previousSnapshot: first,
            refreshMode: ForecastRefreshMode.Incremental);

        Assert.Equal(["01"], simulator.Calls);
        Assert.Equal(0, refreshed.Metrics!.ReusedFreeCompanies);
        Assert.NotEqual(
            first.FcCalculationSettingsFingerprints["01"],
            refreshed.FcCalculationSettingsFingerprints["01"]);
    }

    [Fact]
    public void SimulatorReceivesRoleAwareTargetScope()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(100);
        var fc = CreateFc(1, 50, now.AddDays(1));
        var prototype = fc.Submarines[0];
        fc = fc with
        {
            Submarines =
            [
                prototype with { SubmarineId = 1, Rank = 50 },
                prototype with { SubmarineId = 2, Rank = 50 },
                prototype with { SubmarineId = 3, Rank = 50 },
                prototype with { SubmarineId = 4, Rank = 50 },
                prototype with { SubmarineId = 5, Rank = 100 },
            ],
        };
        fc = fc with { DataFingerprint = FcDataFingerprint.Create(fc) };
        var reader = new MutableStateReader([fc]);
        var simulator = new RecordingSimulator();
        var service = new EtaPlannerService(reader, simulator);
        var request = new PlannerCalculationRequest(
            Settings(),
            new Dictionary<string, FcSimulationOverride>
            {
                ["01"] = new()
                {
                    SubmarineAssignments = new Dictionary<long, SubmarineAssignment>
                    {
                        [2] = SubmarineAssignment.Farming,
                        [3] = SubmarineAssignment.Paused,
                        [4] = SubmarineAssignment.Leveling,
                        [5] = SubmarineAssignment.Leveling,
                    },
                },
            });

        service.Calculate(request, now, CancellationToken.None);

        var scope = Assert.Single(simulator.Scopes);
        Assert.Equal([1L, 4L], scope.Order().ToArray());
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

    [Fact]
    public void HiddenFcRemainsTrackedButIsExcludedFromForecastWork()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(100);
        var reader = new MutableStateReader([CreateFc(1, 50, now.AddDays(1)), CreateFc(0xab, 60, now.AddDays(1))]);
        var simulator = new RecordingSimulator();
        var service = new EtaPlannerService(reader, simulator);
        var request = PlannerCalculationRequest.FromGlobalSettings(Settings()) with
        {
            HiddenFreeCompanyIds = new HashSet<string> { "ab" },
        };

        var snapshot = service.Calculate(request, now, CancellationToken.None);

        Assert.Equal(["01"], simulator.Calls);
        Assert.Equal(["01", "AB"], snapshot.FreeCompanies.Select(fc => fc.FcIdKey).Order().ToArray());
        Assert.Equal("01", Convert.ToHexString(Assert.Single(snapshot.Results).FcId));
        Assert.Equal("01", Assert.Single(snapshot.FcProgress).FcIdKey);
        Assert.Equal(["01"], snapshot.FcCalculationSettingsFingerprints.Keys);
        Assert.Equal(1, snapshot.Metrics!.CalculatedFreeCompanies);
        Assert.True(reader.LastHiddenFreeCompanyIds.Contains("AB"));
    }

    [Fact]
    public void HidingAndUnhidingFcUpdatesIncrementalCalculationScope()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(100);
        var reader = new MutableStateReader([CreateFc(1, 50, now.AddDays(1)), CreateFc(2, 60, now.AddDays(1))]);
        var simulator = new RecordingSimulator();
        var service = new EtaPlannerService(reader, simulator);
        var settings = Settings();
        var initial = service.Calculate(settings, now);
        simulator.Calls.Clear();

        var hiddenRequest = PlannerCalculationRequest.FromGlobalSettings(settings) with
        {
            HiddenFreeCompanyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "02" },
        };
        var hidden = service.Calculate(
            hiddenRequest,
            now.AddMinutes(5),
            CancellationToken.None,
            previousSnapshot: initial,
            refreshMode: ForecastRefreshMode.Incremental);

        Assert.Empty(simulator.Calls);
        Assert.Equal("01", Convert.ToHexString(Assert.Single(hidden.Results).FcId));
        Assert.Equal("01", Assert.Single(hidden.FcProgress).FcIdKey);
        Assert.Equal(1, hidden.Metrics!.ReusedFreeCompanies);

        simulator.Calls.Clear();
        var restored = service.Calculate(
            PlannerCalculationRequest.FromGlobalSettings(settings),
            now.AddMinutes(10),
            CancellationToken.None,
            previousSnapshot: hidden,
            refreshMode: ForecastRefreshMode.Incremental);

        Assert.Equal(["02"], simulator.Calls);
        Assert.Equal(["01", "02"], restored.Results.Select(result => Convert.ToHexString(result.FcId)).Order().ToArray());
        Assert.Equal(1, restored.Metrics!.ReusedFreeCompanies);
        Assert.Equal(1, restored.Metrics.CalculatedFreeCompanies);
    }

    [Fact]
    public void AllHiddenFcsProduceACompleteEmptyForecast()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(100);
        var reader = new MutableStateReader([CreateFc(1, 50, now.AddDays(1)), CreateFc(2, 60, now.AddDays(1))]);
        var simulator = new RecordingSimulator();
        var service = new EtaPlannerService(reader, simulator);
        var request = PlannerCalculationRequest.FromGlobalSettings(Settings()) with
        {
            HiddenFreeCompanyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "01", "02" },
        };

        var snapshot = service.Calculate(request, now, CancellationToken.None);

        Assert.True(snapshot.IsComplete);
        Assert.Equal(2, snapshot.FreeCompanies.Count);
        Assert.Empty(snapshot.Results);
        Assert.Empty(snapshot.FcProgress);
        Assert.Empty(snapshot.FcCalculationSettingsFingerprints);
        Assert.Empty(simulator.Calls);
        Assert.Equal(0, snapshot.Metrics!.CalculatedFreeCompanies);
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

        public IReadOnlySet<string> LastHiddenFreeCompanyIds { get; private set; }
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public SubmarineTrackerDataFingerprint GetDataFingerprint(EtaSettings settings)
            => SubmarineTrackerDataFingerprint.Capture("test.db");

        public IReadOnlyList<FcState> Read(
            EtaSettings settings,
            ICollection<string> warnings,
            IReadOnlySet<string>? hiddenFreeCompanyIds = null)
        {
            LastHiddenFreeCompanyIds = hiddenFreeCompanyIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return FreeCompanies;
        }
    }

    private sealed class RecordingSimulator : IEtaSimulator
    {
        public List<string> Calls { get; } = [];

        public List<IReadOnlySet<long>> Scopes { get; } = [];

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

        public EtaResult Simulate(
            FcState fc,
            EtaSettings settings,
            EtaSimulationScope scope,
            DateTimeOffset now,
            DateTimeOffset? deadlineUtc,
            CancellationToken cancellationToken)
        {
            Scopes.Add(scope.TargetSubmarineIds);
            return Simulate(fc, settings, now, deadlineUtc, cancellationToken);
        }
    }
}
