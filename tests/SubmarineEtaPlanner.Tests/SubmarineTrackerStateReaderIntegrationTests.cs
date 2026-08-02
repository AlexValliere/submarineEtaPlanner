using MessagePack;
using SubmarineEtaPlanner.Planner;
using SubmarineEtaPlanner.TrackerData;
using System.Data.SQLite;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class SubmarineTrackerStateReaderIntegrationTests
{
    [Fact]
    public void ReadsCurrentSubmarineTrackerSchemaFromSqlite()
    {
        var directory = Directory.CreateTempSubdirectory("seta-tracker-schema-");
        try
        {
            var databasePath = Path.Combine(directory.FullName, "submarine-sqlite.db");
            CreateDatabase(databasePath);
            var settings = EtaSettings.CreateDefault() with
            {
                SubmarineTrackerDatabasePathOverride = databasePath,
            };
            var warnings = new List<string>();

            var freeCompanies = new SubmarineTrackerStateReader().Read(settings, warnings);

            Assert.True(freeCompanies.Count == 1, string.Join(Environment.NewLine, warnings));
            var fc = freeCompanies[0];
            Assert.Equal("TEST - Cerberus", fc.DisplayName);
            Assert.Equal(new HashSet<uint> { 1, 3 }, fc.UnlockedPoints);
            Assert.Equal(new HashSet<uint> { 1 }, fc.ExploredPoints);
            var submarine = Assert.Single(fc.Submarines);
            Assert.Equal(42, submarine.SubmarineId);
            Assert.Equal("Forecast Fixture", submarine.Name);
            Assert.Equal(73, submarine.Rank);
            Assert.Equal((uint)1234, submarine.CurrentExp);
            Assert.Equal((uint)5678, submarine.NextLevelExp);
            Assert.Equal(new SubmarineBuildParts(1, 2, 3, 4), submarine.BuildParts);
            Assert.Equal(new uint[] { 1, 3 }, submarine.CurrentRoute);
            Assert.True(submarine.CurrentVoyageKnown);
            Assert.Empty(warnings);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static void CreateDatabase(string path)
    {
        SQLiteConnection.CreateFile(path);
        using var connection = new SQLiteConnection($"Data Source={path}");
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE freecompany (
                    FreeCompanyId BLOB NOT NULL,
                    FreeCompanyTag TEXT NOT NULL,
                    World TEXT NOT NULL,
                    UnlockedSectors BLOB NOT NULL,
                    ExploredSectors BLOB NOT NULL
                );
                CREATE TABLE submarine (
                    FreeCompanyId BLOB NOT NULL,
                    SubmarineId INTEGER NOT NULL,
                    Return INTEGER NOT NULL,
                    Name TEXT NOT NULL,
                    Rank INTEGER NOT NULL,
                    Route BLOB NOT NULL,
                    Hull INTEGER NOT NULL,
                    Stern INTEGER NOT NULL,
                    Bow INTEGER NOT NULL,
                    Bridge INTEGER NOT NULL,
                    CExp INTEGER NOT NULL,
                    NExp INTEGER NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        var fcId = new byte[] { 0x10, 0x20 };
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO freecompany (FreeCompanyId, FreeCompanyTag, World, UnlockedSectors, ExploredSectors)
                VALUES (@fc, 'TEST', 'Cerberus', @unlocked, @explored)
                """;
            command.Parameters.AddWithValue("@fc", fcId);
            command.Parameters.AddWithValue(
                "@unlocked",
                MessagePackSerializer.Serialize(new Dictionary<uint, bool> { [1] = true, [2] = false, [3] = true }));
            command.Parameters.AddWithValue(
                "@explored",
                MessagePackSerializer.Serialize(new Dictionary<uint, bool> { [1] = true, [3] = false }));
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO submarine
                    (FreeCompanyId, SubmarineId, Return, Name, Rank, Route, Hull, Stern, Bow, Bridge, CExp, NExp)
                VALUES
                    (@fc, 42, @return, 'Forecast Fixture', 73, @route, 1, 2, 3, 4, 1234, 5678)
                """;
            command.Parameters.AddWithValue("@fc", fcId);
            command.Parameters.AddWithValue("@return", DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds());
            command.Parameters.AddWithValue("@route", MessagePackSerializer.Serialize(new uint[] { 1, 3 }));
            command.ExecuteNonQuery();
        }
    }
}
