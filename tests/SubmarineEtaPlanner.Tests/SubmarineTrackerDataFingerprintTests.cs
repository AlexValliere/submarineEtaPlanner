using SubmarineEtaPlanner.TrackerData;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class SubmarineTrackerDataFingerprintTests
{
    [Fact]
    public void FingerprintChangesWhenDatabaseOrWalChanges()
    {
        var directory = Directory.CreateTempSubdirectory("seta-fingerprint-");
        try
        {
            var databasePath = Path.Combine(directory.FullName, "submarine-sqlite.db");
            var missing = SubmarineTrackerDataFingerprint.Capture(databasePath);

            File.WriteAllText(databasePath, "database");
            var databaseCreated = SubmarineTrackerDataFingerprint.Capture(databasePath);

            File.WriteAllText($"{databasePath}-wal", "wal");
            var walCreated = SubmarineTrackerDataFingerprint.Capture(databasePath);

            Assert.NotEqual(missing, databaseCreated);
            Assert.NotEqual(databaseCreated, walCreated);
            Assert.True(databaseCreated.Database.Exists);
            Assert.False(databaseCreated.WriteAheadLog.Exists);
            Assert.True(walCreated.WriteAheadLog.Exists);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
