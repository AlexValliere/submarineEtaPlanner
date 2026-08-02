namespace SubmarineEtaPlanner;

internal readonly record struct SubmarineTrackerDependencyState(bool IsInstalled, bool IsLoaded)
{
    public bool IsAvailable => IsInstalled && IsLoaded;
}
