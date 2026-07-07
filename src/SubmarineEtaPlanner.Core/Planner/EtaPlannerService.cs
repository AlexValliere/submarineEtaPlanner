using SubmarineEtaPlanner.TrackerData;

namespace SubmarineEtaPlanner.Planner;

public sealed class EtaPlannerService(SubmarineTrackerStateReader stateReader, EtaSimulator simulator)
{
    public EtaPlannerSnapshot Calculate(EtaSettings settings, DateTimeOffset now)
    {
        var warnings = new List<string>();
        var fcStates = stateReader.Read(settings, warnings);
        var results = fcStates.Select(fc => simulator.Simulate(fc, settings, now)).ToArray();
        return new EtaPlannerSnapshot(now, fcStates, results, warnings);
    }
}

public sealed record EtaPlannerSnapshot(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<FcState> FreeCompanies,
    IReadOnlyList<EtaResult> Results,
    IReadOnlyList<string> Warnings);
