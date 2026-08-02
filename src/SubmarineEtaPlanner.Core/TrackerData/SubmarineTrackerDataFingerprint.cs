namespace SubmarineEtaPlanner.TrackerData;

public sealed record SubmarineTrackerDataFingerprint(
    string DatabasePath,
    TrackerFileFingerprint Database,
    TrackerFileFingerprint WriteAheadLog)
{
    public static SubmarineTrackerDataFingerprint Capture(string databasePath)
    {
        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(databasePath);
        }
        catch
        {
            normalizedPath = databasePath;
        }

        return new SubmarineTrackerDataFingerprint(
            normalizedPath,
            TrackerFileFingerprint.Capture(normalizedPath),
            TrackerFileFingerprint.Capture($"{normalizedPath}-wal"));
    }
}

public readonly record struct TrackerFileFingerprint(bool Exists, long Length, long LastWriteUtcTicks)
{
    public static TrackerFileFingerprint Capture(string path)
    {
        try
        {
            var file = new FileInfo(path);
            file.Refresh();
            return file.Exists
                ? new TrackerFileFingerprint(true, file.Length, file.LastWriteTimeUtc.Ticks)
                : new TrackerFileFingerprint(false, 0, 0);
        }
        catch
        {
            return new TrackerFileFingerprint(false, 0, 0);
        }
    }
}
