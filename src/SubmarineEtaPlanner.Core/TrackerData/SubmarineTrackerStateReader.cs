using System.Data.SQLite;
using MessagePack;
using SubmarineEtaPlanner.Planner;

namespace SubmarineEtaPlanner.TrackerData;

public interface ISubmarineTrackerStateReader
{
    SubmarineTrackerDataFingerprint GetDataFingerprint(EtaSettings settings);

    IReadOnlyList<FcState> Read(EtaSettings settings, ICollection<string> warnings);
}

public sealed class SubmarineTrackerStateReader(ISalvageValueCatalog? salvageValueCatalog = null) : ISubmarineTrackerStateReader
{
    private readonly IReadOnlyList<SalvageItemValue> salvageItems =
        (salvageValueCatalog ?? KnownSalvageValueCatalog.Instance).Items;

    public SubmarineTrackerDataFingerprint GetDataFingerprint(EtaSettings settings)
        => SubmarineTrackerDataFingerprint.Capture(ResolveDatabasePath(settings));

    public IReadOnlyList<FcState> Read(EtaSettings settings, ICollection<string> warnings)
    {
        var dbPath = ResolveDatabasePath(settings);
        if (!File.Exists(dbPath))
        {
            warnings.Add($"SubmarineTracker database was not found at {dbPath}.");
            return [];
        }

        try
        {
            using var connection = OpenReadOnly(dbPath);
            using var transaction = connection.BeginTransaction();
            var fcs = ReadFreeCompanies(connection, transaction, warnings);
            var salvage = ReadSalvageSummaries(connection, transaction, warnings);
            var subs = ReadSubmarines(connection, transaction, settings, warnings, salvage)
                .GroupBy(s => Convert.ToHexString(s.FcId))
                .ToDictionary(g => g.Key, g => (IReadOnlyList<SubmarineState>)g.OrderBy(s => s.Name).ToArray());

            var states = fcs.Select(fc =>
            {
                subs.TryGetValue(fc.FcIdKey, out var fcSubs);
                var complete = fc with { Submarines = fcSubs ?? [] };
                return complete with { DataFingerprint = FcDataFingerprint.Create(complete) };
            }).OrderBy(fc => fc.DisplayName).ToArray();
            transaction.Commit();
            return states;
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not read SubmarineTracker database: {ex.Message}");
            return [];
        }
    }

    public string ResolveDatabasePath(EtaSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.SubmarineTrackerDatabasePathOverride))
            return Environment.ExpandEnvironmentVariables(settings.SubmarineTrackerDatabasePathOverride);

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "XIVLauncher", "pluginConfigs", "SubmarineTracker", "submarine-sqlite.db");
    }

    private static IReadOnlyList<FcState> ReadFreeCompanies(
        SQLiteConnection connection,
        SQLiteTransaction transaction,
        ICollection<string> warnings)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT FreeCompanyId, FreeCompanyTag, World, UnlockedSectors, ExploredSectors FROM freecompany";

        using var reader = command.ExecuteReader();
        var fcs = new List<FcState>();
        while (reader.Read())
        {
            var fcId = (byte[])reader["FreeCompanyId"];
            var unlocked = DecodeDictionaryKeys((byte[])reader["UnlockedSectors"], warnings, "UnlockedSectors");
            var explored = DecodeDictionaryKeys((byte[])reader["ExploredSectors"], warnings, "ExploredSectors");
            var unlockDataKnown = unlocked.Count > 0;
            if (!unlockDataKnown)
                warnings.Add($"Unlock data is missing for {reader.GetString(1)}; its leveling ETA is incomplete.");

            fcs.Add(new FcState(
                fcId,
                reader.GetString(1),
                reader.GetString(2),
                unlocked,
                explored,
                [])
            {
                UnlockDataKnown = unlockDataKnown,
            });
        }

        return fcs;
    }

    private static IReadOnlyList<SubmarineState> ReadSubmarines(
        SQLiteConnection connection,
        SQLiteTransaction transaction,
        EtaSettings settings,
        ICollection<string> warnings,
        IReadOnlyDictionary<TrackedSubmarineKey, SubmarineSalvageSummary> salvage)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT FreeCompanyId, SubmarineId, Return, Name, Rank, Route, Hull, Stern, Bow, Bridge, CExp, NExp
            FROM submarine
            """;

        using var reader = command.ExecuteReader();
        var subs = new List<SubmarineState>();
        while (reader.Read())
        {
            var route = DecodeRoute((byte[])reader["Route"], warnings);
            var returnAt = UnixSecondsToUtc(Convert.ToInt64(reader["Return"]));
            var fcId = (byte[])reader["FreeCompanyId"];
            var name = reader.GetString(3);
            var submarineId = Convert.ToInt64(reader["SubmarineId"]);
            var manualOverride = GetManualOverride(settings, fcId, submarineId);
            if (manualOverride.Count > 0)
                route = manualOverride;

            var currentVoyageKnown = returnAt <= DateTimeOffset.UtcNow || route.Count > 0;

            var state = new SubmarineState(
                fcId,
                submarineId,
                name,
                Convert.ToInt32(reader["Rank"]),
                Convert.ToUInt32(reader["CExp"]),
                Convert.ToUInt32(reader["NExp"]),
                new SubmarineBuildParts(
                    Convert.ToUInt16(reader["Hull"]),
                    Convert.ToUInt16(reader["Stern"]),
                    Convert.ToUInt16(reader["Bow"]),
                    Convert.ToUInt16(reader["Bridge"])),
                returnAt,
                route,
                currentVoyageKnown,
                manualOverride)
            {
                Salvage = salvage.GetValueOrDefault(new TrackedSubmarineKey(Convert.ToHexString(fcId), submarineId)) ??
                          SubmarineSalvageSummary.Empty,
            };
            subs.Add(state);
        }

        return subs;
    }

    private IReadOnlyDictionary<TrackedSubmarineKey, SubmarineSalvageSummary> ReadSalvageSummaries(
        SQLiteConnection connection,
        SQLiteTransaction transaction,
        ICollection<string> warnings)
    {
        if (this.salvageItems.Count == 0 || !TableExists(connection, transaction, "loot"))
            return new Dictionary<TrackedSubmarineKey, SubmarineSalvageSummary>();

        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            var parameters = this.salvageItems.Select((_, index) => $"@item{index}").ToArray();
            command.CommandText = $"""
                WITH salvage AS (
                    SELECT FreeCompanyId, SubmarineId, Return, PrimaryItem AS ItemId, PrimaryCount AS Quantity
                    FROM loot
                    WHERE Valid = 1 AND PrimaryCount > 0 AND PrimaryItem IN ({string.Join(", ", parameters)})
                    UNION ALL
                    SELECT FreeCompanyId, SubmarineId, Return, AdditionalItem AS ItemId, AdditionalCount AS Quantity
                    FROM loot
                    WHERE Valid = 1 AND AdditionalCount > 0 AND AdditionalItem IN ({string.Join(", ", parameters)})
                )
                SELECT FreeCompanyId, SubmarineId, Return, ItemId, SUM(Quantity) AS Quantity
                FROM salvage
                GROUP BY FreeCompanyId, SubmarineId, Return, ItemId
                ORDER BY FreeCompanyId, SubmarineId, Return, ItemId
                """;
            for (var index = 0; index < this.salvageItems.Count; index++)
                command.Parameters.AddWithValue(parameters[index], this.salvageItems[index].ItemId);

            var itemValues = this.salvageItems.ToDictionary(item => item.ItemId);
            var builders = new Dictionary<TrackedSubmarineKey, SalvageSummaryBuilder>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var key = new TrackedSubmarineKey(
                    Convert.ToHexString((byte[])reader["FreeCompanyId"]),
                    Convert.ToInt64(reader["SubmarineId"]));
                if (!builders.TryGetValue(key, out var builder))
                {
                    builder = new SalvageSummaryBuilder();
                    builders[key] = builder;
                }

                var itemId = Convert.ToUInt32(reader["ItemId"]);
                if (itemValues.TryGetValue(itemId, out var item))
                {
                    builder.Add(
                        UnixSecondsToUtc(Convert.ToInt64(reader["Return"])),
                        item,
                        Convert.ToInt64(reader["Quantity"]));
                }
            }

            return builders.ToDictionary(pair => pair.Key, pair => pair.Value.Build());
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not read SubmarineTracker loot history: {ex.Message} Recorded salvage value is unavailable.");
            return new Dictionary<TrackedSubmarineKey, SubmarineSalvageSummary>();
        }
    }

    private static bool TableExists(SQLiteConnection connection, SQLiteTransaction transaction, string tableName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name";
        command.Parameters.AddWithValue("@name", tableName);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static SQLiteConnection OpenReadOnly(string dbPath)
    {
        var builder = new SQLiteConnectionStringBuilder
        {
            DataSource = dbPath,
            ReadOnly = true,
        };

        var connection = new SQLiteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    private static IReadOnlySet<uint> DecodeDictionaryKeys(byte[] blob, ICollection<string> warnings, string fieldName)
    {
        try
        {
            var value = MessagePackSerializer.Deserialize<Dictionary<uint, bool>>(blob);
            return value.Where(pair => pair.Value).Select(pair => pair.Key).ToHashSet();
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not decode {fieldName}: {ex.Message}");
            return new HashSet<uint>();
        }
    }

    private static IReadOnlyList<uint> DecodeRoute(byte[] blob, ICollection<string> warnings)
    {
        if (blob.Length == 0)
            return [];

        try
        {
            return MessagePackSerializer.Deserialize<uint[]>(blob);
        }
        catch
        {
            try
            {
                return MessagePackSerializer.Deserialize<List<uint>>(blob);
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not decode a submarine route: {ex.Message}");
                return [];
            }
        }
    }

    private static DateTimeOffset UnixSecondsToUtc(long seconds)
        => seconds <= 0 ? DateTimeOffset.MinValue : DateTimeOffset.FromUnixTimeSeconds(seconds).ToUniversalTime();

    private static IReadOnlyList<uint> GetManualOverride(EtaSettings settings, byte[] fcId, long submarineId)
    {
        var fcHex = Convert.ToHexString(fcId);
        var keys = new[]
        {
            $"{fcHex}:{submarineId}",
            submarineId.ToString(),
        };

        foreach (var key in keys)
        {
            if (settings.ManualCurrentRouteOverrides.TryGetValue(key, out var route))
                return route.Where(point => point > 0).Distinct().ToArray();
        }

        return [];
    }

    private readonly record struct TrackedSubmarineKey(string FcIdKey, long SubmarineId);

    private sealed class SalvageSummaryBuilder
    {
        private readonly HashSet<DateTimeOffset> returns = [];
        private readonly Dictionary<uint, (SalvageItemValue Item, long Quantity)> quantities = [];

        public void Add(DateTimeOffset returnAtUtc, SalvageItemValue item, long quantity)
        {
            this.returns.Add(returnAtUtc);
            if (this.quantities.TryGetValue(item.ItemId, out var current))
                this.quantities[item.ItemId] = (item, checked(current.Quantity + quantity));
            else
                this.quantities[item.ItemId] = (item, quantity);
        }

        public SubmarineSalvageSummary Build()
        {
            var orderedReturns = this.returns.Order().ToArray();
            var items = this.quantities.Values
                .Select(value => new SalvageItemTotal(
                    value.Item.ItemId,
                    value.Item.Name,
                    value.Item.NpcSalePrice,
                    value.Quantity))
                .OrderBy(item => item.ItemId)
                .ToArray();
            return new SubmarineSalvageSummary(
                orderedReturns.Length,
                orderedReturns.FirstOrDefault() == default ? null : orderedReturns[0],
                orderedReturns.LastOrDefault() == default ? null : orderedReturns[^1],
                items);
        }
    }
}
