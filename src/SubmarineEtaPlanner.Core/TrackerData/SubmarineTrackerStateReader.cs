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
            var voyageHistory = ReadVoyageHistory(connection, transaction, warnings);
            var subs = ReadSubmarines(connection, transaction, settings, warnings, voyageHistory.Observations)
                .GroupBy(s => Convert.ToHexString(s.FcId))
                .ToDictionary(g => g.Key, g => (IReadOnlyList<SubmarineState>)g.OrderBy(s => s.Name).ToArray());

            var states = fcs.Select(fc =>
            {
                subs.TryGetValue(fc.FcIdKey, out var fcSubs);
                var complete = fc with { Submarines = fcSubs ?? [], IncomeHistory = voyageHistory.State };
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
                GameFreeCompanyId = FreeCompanyIdDecoder.TryDecode(fcId),
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
        IReadOnlyDictionary<TrackedSubmarineKey, IReadOnlyList<VoyageObservation>> voyageHistory)
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
            var returnSeconds = Convert.ToInt64(reader["Return"]);
            var hasValidReturn = returnSeconds > 0;
            var returnAt = UnixSecondsToUtc(returnSeconds);
            var fcId = (byte[])reader["FreeCompanyId"];
            var name = reader.GetString(3);
            var submarineId = Convert.ToInt64(reader["SubmarineId"]);
            var manualOverride = GetManualOverride(settings, fcId, submarineId);
            if (!hasValidReturn)
                route = [];
            else if (manualOverride.Count > 0)
                route = manualOverride;

            var currentVoyageKnown = hasValidReturn &&
                                     (returnAt <= DateTimeOffset.UtcNow || route.Count > 0);
            var key = new TrackedSubmarineKey(Convert.ToHexString(fcId), submarineId);
            var history = voyageHistory.GetValueOrDefault(key) ?? [];

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
                Salvage = DeriveSalvageSummary(history),
                VoyageHistory = history,
            };
            subs.Add(state);
        }

        return subs;
    }

    private sealed record VoyageHistoryReadResult(
        IReadOnlyDictionary<TrackedSubmarineKey, IReadOnlyList<VoyageObservation>> Observations,
        IncomeHistoryReadState State);

    private VoyageHistoryReadResult ReadVoyageHistory(
        SQLiteConnection connection,
        SQLiteTransaction transaction,
        ICollection<string> warnings)
    {
        try
        {
            if (!TableExists(connection, transaction, "loot"))
                return new(new Dictionary<TrackedSubmarineKey, IReadOnlyList<VoyageObservation>>(),
                    new(IncomeHistoryReadStatus.Unavailable, "SubmarineTracker has no loot history table."));

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT
                    FreeCompanyId,
                    SubmarineId,
                    Return,
                    Sector,
                    Rank,
                    Surv,
                    Ret,
                    Fav,
                    PrimaryItem,
                    PrimaryCount,
                    AdditionalItem,
                    AdditionalCount
                FROM loot
                WHERE Valid = 1
                ORDER BY FreeCompanyId, SubmarineId, Return, Sector
                """;

            var rows = new List<VoyageObservationRawRow>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new VoyageObservationRawRow(
                    (byte[])reader["FreeCompanyId"],
                    Convert.ToInt64(reader["SubmarineId"]),
                    UnixSecondsToUtc(Convert.ToInt64(reader["Return"])),
                    Convert.ToUInt32(reader["Sector"]),
                    Convert.ToInt32(reader["Rank"]),
                    Convert.ToInt32(reader["Surv"]),
                    Convert.ToInt32(reader["Ret"]),
                    Convert.ToInt32(reader["Fav"]),
                    Convert.ToUInt32(reader["PrimaryItem"]),
                    Convert.ToInt64(reader["PrimaryCount"]),
                    Convert.ToUInt32(reader["AdditionalItem"]),
                    Convert.ToInt64(reader["AdditionalCount"])));
            }

            var observations = VoyageObservationBuilder.Build(rows, this.salvageItems, warnings)
                .GroupBy(observation => new TrackedSubmarineKey(observation.FcIdKey, observation.SubmarineId))
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<VoyageObservation>)group.OrderBy(observation => observation.ReturnAtUtc).ToArray());
            return new(observations, IncomeHistoryReadState.Available);
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not read SubmarineTracker loot history: {ex.Message} Voyage history and recorded salvage value are unavailable.");
            return new(new Dictionary<TrackedSubmarineKey, IReadOnlyList<VoyageObservation>>(),
                new(IncomeHistoryReadStatus.Unavailable, $"Could not read SubmarineTracker loot history: {ex.Message}"));
        }
    }

    private static SubmarineSalvageSummary DeriveSalvageSummary(IReadOnlyList<VoyageObservation> observations)
    {
        if (observations.Count == 0)
            return SubmarineSalvageSummary.Empty;

        var ordered = observations.OrderBy(observation => observation.ReturnAtUtc).ToArray();
        var items = ordered
            .SelectMany(observation => observation.Items)
            .GroupBy(item => item.ItemId)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var first = group.First();
                return new SalvageItemTotal(
                    first.ItemId,
                    first.Name,
                    first.NpcSalePrice,
                    group.Sum(item => item.Quantity));
            })
            .ToArray();
        return new SubmarineSalvageSummary(
            ordered.Length,
            ordered[0].ReturnAtUtc,
            ordered[^1].ReturnAtUtc,
            items)
        {
            Voyages = ordered.Select(observation => new SalvageVoyageRecord(
                observation.FcIdKey,
                observation.SubmarineId,
                observation.ReturnAtUtc,
                observation.Items)).ToArray(),
        };
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
}
