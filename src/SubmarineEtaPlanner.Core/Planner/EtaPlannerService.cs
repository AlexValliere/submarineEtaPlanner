using SubmarineEtaPlanner.TrackerData;

namespace SubmarineEtaPlanner.Planner;

public sealed class EtaPlannerService(SubmarineTrackerStateReader stateReader, EtaSimulator simulator)
{
    public EtaPlannerSnapshot Calculate(EtaSettings settings, DateTimeOffset now)
    {
        var warnings = new List<string>();
        var fcStates = stateReader.Read(settings, warnings);
        var deadlineUtc = settings.CalculationTimeLimitSeconds > 0
            ? DateTimeOffset.UtcNow.AddSeconds(settings.CalculationTimeLimitSeconds)
            : (DateTimeOffset?)null;
        var results = new List<EtaResult>();

        foreach (var fc in fcStates)
        {
            if (IsTimedOut(deadlineUtc))
            {
                warnings.Add($"Calculation stopped after {settings.CalculationTimeLimitSeconds} seconds. Results are partial.");
                break;
            }

            results.Add(simulator.Simulate(fc, settings, now, deadlineUtc));
        }

        return new EtaPlannerSnapshot(now, fcStates, results, warnings);
    }

    private static bool IsTimedOut(DateTimeOffset? deadlineUtc)
        => deadlineUtc is not null && DateTimeOffset.UtcNow >= deadlineUtc.Value;
}

public sealed record EtaPlannerSnapshot(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<FcState> FreeCompanies,
    IReadOnlyList<EtaResult> Results,
    IReadOnlyList<string> Warnings);
