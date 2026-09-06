namespace SubmarineEtaPlanner.Planner;

internal static class OperationsReturnWindowPreferences
{
    public const int DefaultHours = 4;
    public static IReadOnlyList<int> SupportedHours { get; } = Array.AsReadOnly(new[] { 1, 2, 4, 8, 24 });

    public static int Normalize(int hours) => SupportedHours.Contains(hours) ? hours : DefaultHours;
}
