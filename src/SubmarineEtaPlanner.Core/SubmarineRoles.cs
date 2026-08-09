namespace SubmarineEtaPlanner;

public enum EffectiveSubmarineRole
{
    Leveling,
    Farming,
    Paused,
}

public sealed record FcRoleSummary(
    int LevelingCount,
    int FarmingCount,
    int PausedCount)
{
    public bool HasLeveling => LevelingCount > 0;
    public bool HasFarming => FarmingCount > 0;
    public bool HasPaused => PausedCount > 0;
}

public static class FcRoleSummaryFormatter
{
    public static string Format(FcRoleSummary summary)
    {
        var roles = new List<string>(3);
        if (summary.FarmingCount > 0)
            roles.Add($"{summary.FarmingCount} farming");
        if (summary.LevelingCount > 0)
            roles.Add($"{summary.LevelingCount} leveling");
        if (summary.PausedCount > 0)
            roles.Add($"{summary.PausedCount} paused");
        return roles.Count == 0 ? "No submarines" : string.Join(" · ", roles);
    }
}

public static class SubmarineRoleResolver
{
    public static EffectiveSubmarineRole Resolve(
        SubmarineAssignment assignment,
        int rank,
        int effectiveTargetRank)
        => assignment switch
        {
            SubmarineAssignment.Auto when rank < effectiveTargetRank => EffectiveSubmarineRole.Leveling,
            SubmarineAssignment.Auto => EffectiveSubmarineRole.Farming,
            SubmarineAssignment.Leveling => EffectiveSubmarineRole.Leveling,
            SubmarineAssignment.Farming => EffectiveSubmarineRole.Farming,
            SubmarineAssignment.Paused => EffectiveSubmarineRole.Paused,
            _ => throw new ArgumentOutOfRangeException(nameof(assignment), assignment, "Unknown submarine assignment."),
        };
}
