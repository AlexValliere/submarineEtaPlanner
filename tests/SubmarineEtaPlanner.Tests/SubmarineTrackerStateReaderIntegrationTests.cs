using MessagePack;
using SubmarineEtaPlanner.Planner;
using SubmarineEtaPlanner.TrackerData;
using System.Data.SQLite;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class SubmarineTrackerStateReaderIntegrationTests
{
    private const ulong GameFreeCompanyId = 9_876_543_210;

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
            Assert.Equal(IncomeHistoryReadStatus.Available, fc.IncomeHistory.Status);
            var expectedRawFcId = MessagePackSerializer.Serialize(GameFreeCompanyId);
            Assert.Equal(expectedRawFcId, fc.FcId);
            Assert.Equal(Convert.ToHexString(expectedRawFcId), fc.FcIdKey);
            Assert.Equal(GameFreeCompanyId, fc.GameFreeCompanyId);
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
            Assert.Equal(3, submarine.VoyageHistory.Count);
            var firstVoyage = submarine.VoyageHistory[0];
            Assert.Equal(GameFreeCompanyId, firstVoyage.GameFreeCompanyId);
            Assert.Equal(42, firstVoyage.SubmarineId);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), firstVoyage.ReturnAtUtc);
            Assert.Equal(new uint[] { 2, 7 }, firstVoyage.SectorIds);
            Assert.Equal(73, firstVoyage.Rank);
            Assert.Equal(150, firstVoyage.Surveillance);
            Assert.Equal(120, firstVoyage.Retrieval);
            Assert.Equal(90, firstVoyage.Favor);
            Assert.Collection(
                firstVoyage.Items,
                item =>
                {
                    Assert.Equal(22500u, item.ItemId);
                    Assert.Equal(3, item.Quantity);
                },
                item =>
                {
                    Assert.Equal(22501u, item.ItemId);
                    Assert.Equal(4, item.Quantity);
                });
            Assert.DoesNotContain(
                submarine.VoyageHistory,
                observation => observation.ReturnAtUtc == DateTimeOffset.FromUnixTimeSeconds(1700200000));
            Assert.Contains(submarine.VoyageHistory, voyage => voyage.GrossNpcGil == 0);
            Assert.Equal(3, submarine.Salvage.VoyageCount);
            Assert.Equal(3, submarine.Salvage.Voyages.Count);
            Assert.Contains(submarine.Salvage.Voyages, voyage => voyage.GrossNpcGil == 0);
            Assert.Equal(9, submarine.Salvage.ItemCount);
            Assert.Equal(81_000, submarine.Salvage.TotalGil);
            Assert.Equal(4, submarine.Salvage.Items.Single(item => item.ItemId == 22500).Quantity);
            Assert.Equal(8_000u, submarine.Salvage.Items.Single(item => item.ItemId == 22500).NpcSalePrice);
            Assert.Equal(submarine.VoyageHistory.Sum(voyage => voyage.GrossNpcGil), submarine.Salvage.TotalGil);
            Assert.Equal(
                submarine.VoyageHistory.SelectMany(voyage => voyage.Items).Sum(item => item.Quantity),
                submarine.Salvage.ItemCount);
            Assert.Equal(
                submarine.VoyageHistory
                    .SelectMany(voyage => voyage.Items)
                    .GroupBy(item => item.ItemId)
                    .OrderBy(group => group.Key)
                    .Select(group => (ItemId: group.Key, Quantity: group.Sum(item => item.Quantity))),
                submarine.Salvage.Items.Select(item => (item.ItemId, item.Quantity)));
            Assert.Equal(
                submarine.VoyageHistory.Select(voyage => (voyage.ReturnAtUtc, voyage.GrossNpcGil)),
                submarine.Salvage.Voyages.Select(voyage => (voyage.ReturnAtUtc, voyage.GrossNpcGil)));
            Assert.Equal(81_000, fc.RecordedSalvageGil);
            Assert.False(fc.DataFingerprint.IsEmpty);
            Assert.Equal(
                fc.DataFingerprint,
                new SubmarineTrackerStateReader().Read(settings, new List<string>())[0].DataFingerprint);

            using (var connection = new SQLiteConnection($"Data Source={databasePath}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE loot SET PrimaryCount = 3 WHERE SubmarineId = 42 AND Return = 1700000000 AND Sector = 7";
                command.ExecuteNonQuery();
            }

            var lootChanged = new SubmarineTrackerStateReader().Read(settings, new List<string>())[0];
            Assert.Equal(89_000, Assert.Single(lootChanged.Submarines).Salvage.TotalGil);
            Assert.Equal(fc.DataFingerprint, lootChanged.DataFingerprint);

            using (var connection = new SQLiteConnection($"Data Source={databasePath}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE submarine SET Rank = 74 WHERE SubmarineId = 42";
                command.ExecuteNonQuery();
            }

            var changed = new SubmarineTrackerStateReader().Read(settings, new List<string>())[0];
            Assert.NotEqual(fc.DataFingerprint, changed.DataFingerprint);

            using (var connection = new SQLiteConnection($"Data Source={databasePath}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE submarine SET Return = 0 WHERE SubmarineId = 42";
                command.ExecuteNonQuery();
            }

            var transitionSettings = settings with
            {
                ManualCurrentRouteOverrides = new Dictionary<string, List<uint>>
                {
                    ["42"] = [7, 9],
                },
            };
            var transition = Assert.Single(
                new SubmarineTrackerStateReader()
                    .Read(transitionSettings, new List<string>())[0]
                    .Submarines);
            Assert.Equal(DateTimeOffset.MinValue, transition.ReturnAtUtc);
            Assert.Empty(transition.CurrentRoute);
            Assert.False(transition.CurrentVoyageKnown);
            Assert.Equal([7u, 9u], transition.ManualCurrentRouteOverride);
            Assert.Empty(warnings);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void MissingLootTableProducesEmptyHistoryWithoutFailure()
    {
        var directory = Directory.CreateTempSubdirectory("seta-tracker-no-loot-");
        try
        {
            var databasePath = Path.Combine(directory.FullName, "submarine-sqlite.db");
            CreateDatabase(databasePath);
            Execute(databasePath, "DROP TABLE loot");
            var settings = EtaSettings.CreateDefault() with
            {
                SubmarineTrackerDatabasePathOverride = databasePath,
            };
            var warnings = new List<string>();

            var fc = Assert.Single(new SubmarineTrackerStateReader().Read(settings, warnings));

            var submarine = Assert.Single(fc.Submarines);
            Assert.Empty(submarine.VoyageHistory);
            Assert.Equal(SubmarineSalvageSummary.Empty, submarine.Salvage);
            Assert.Equal(IncomeHistoryReadStatus.Unavailable, fc.IncomeHistory.Status);
            Assert.Contains("no loot history table", fc.IncomeHistory.Reason);
            Assert.Empty(warnings);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void MalformedLootTableWarnsButOperationalStateRemainsAvailable()
    {
        var directory = Directory.CreateTempSubdirectory("seta-tracker-malformed-loot-");
        try
        {
            var databasePath = Path.Combine(directory.FullName, "submarine-sqlite.db");
            CreateDatabase(databasePath);
            Execute(databasePath, "ALTER TABLE loot DROP COLUMN Sector");
            var settings = EtaSettings.CreateDefault() with
            {
                SubmarineTrackerDatabasePathOverride = databasePath,
            };
            var warnings = new List<string>();

            var fc = Assert.Single(new SubmarineTrackerStateReader().Read(settings, warnings));

            var submarine = Assert.Single(fc.Submarines);
            Assert.Equal(73, submarine.Rank);
            Assert.Empty(submarine.VoyageHistory);
            Assert.Equal(SubmarineSalvageSummary.Empty, submarine.Salvage);
            var warning = Assert.Single(warnings);
            Assert.Equal(IncomeHistoryReadStatus.Unavailable, fc.IncomeHistory.Status);
            Assert.Contains("Could not read", fc.IncomeHistory.Reason);
            Assert.Contains("Could not read SubmarineTracker loot history", warning);
            Assert.Contains("Voyage history and recorded salvage value are unavailable", warning);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void InvalidFreeCompanyIdDoesNotPreventReadingTheFreeCompanyAndSubmarine()
    {
        var directory = Directory.CreateTempSubdirectory("seta-tracker-invalid-fc-id-");
        try
        {
            var databasePath = Path.Combine(directory.FullName, "submarine-sqlite.db");
            var invalidFcId = new byte[] { 0xc1 };
            CreateDatabase(databasePath, invalidFcId);
            var settings = EtaSettings.CreateDefault() with
            {
                SubmarineTrackerDatabasePathOverride = databasePath,
            };
            var warnings = new List<string>();

            var fc = Assert.Single(new SubmarineTrackerStateReader().Read(settings, warnings));

            Assert.Equal(invalidFcId, fc.FcId);
            Assert.Equal(Convert.ToHexString(invalidFcId), fc.FcIdKey);
            Assert.Null(fc.GameFreeCompanyId);
            Assert.Single(fc.Submarines);
            Assert.Empty(warnings);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void EmptyLootTableIsAvailableWithNoRecordedReturns()
    {
        var directory = Directory.CreateTempSubdirectory("seta-tracker-empty-loot-");
        try
        {
            var databasePath = Path.Combine(directory.FullName, "submarine-sqlite.db");
            CreateDatabase(databasePath);
            Execute(databasePath, "DELETE FROM loot");
            var settings = EtaSettings.CreateDefault() with { SubmarineTrackerDatabasePathOverride = databasePath };
            var warnings = new List<string>();
            var fc = Assert.Single(new SubmarineTrackerStateReader().Read(settings, warnings));
            Assert.Equal(IncomeHistoryReadStatus.Available, fc.IncomeHistory.Status);
            Assert.Empty(Assert.Single(fc.Submarines).VoyageHistory);
            Assert.Empty(warnings);
        }
        finally { directory.Delete(recursive: true); }
    }

    private static void CreateDatabase(string path, byte[]? freeCompanyId = null)
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
                CREATE TABLE loot (
                    FreeCompanyId BLOB NOT NULL,
                    SubmarineId INTEGER NOT NULL,
                    Return INTEGER NOT NULL,
                    Sector INTEGER NOT NULL,
                    Rank INTEGER NOT NULL,
                    Surv INTEGER NOT NULL,
                    Ret INTEGER NOT NULL,
                    Fav INTEGER NOT NULL,
                    PrimaryItem INTEGER NOT NULL,
                    PrimaryCount INTEGER NOT NULL,
                    AdditionalItem INTEGER NOT NULL,
                    AdditionalCount INTEGER NOT NULL,
                    Valid BOOLEAN NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        var fcId = freeCompanyId ?? MessagePackSerializer.Serialize(GameFreeCompanyId);
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

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO loot
                    (FreeCompanyId, SubmarineId, Return, Sector, Rank, Surv, Ret, Fav,
                     PrimaryItem, PrimaryCount, AdditionalItem, AdditionalCount, Valid)
                VALUES
                    (@fc, 42, 1700000000, 7, 73, 150, 120, 90, 22500, 2, 22501, 3, 1),
                    (@fc, 42, 1700000000, 2, 73, 150, 120, 90, 22501, 1, 22500, 1, 1),
                    (@fc, 42, 1700100000, 9, 74, 155, 125, 95, 22503, 1, 22500, 1, 1),
                    (@fc, 42, 1700200000, 4, 72, 140, 110, 80, 22507, 10, 0, 0, 0),
                    (@fc, 42, 1700300000, 12, 75, 160, 130, 100, 5069, 999, 0, 0, 1)
                """;
            command.Parameters.AddWithValue("@fc", fcId);
            command.ExecuteNonQuery();
        }
    }

    private static void Execute(string databasePath, string commandText)
    {
        using var connection = new SQLiteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }
}
