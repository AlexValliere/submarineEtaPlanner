using System.Data.SQLite;
using MessagePack;
using SubmarineEtaPlanner.Planner;

namespace SubmarineEtaPlanner.TrackerData;

public interface ISubmarineTrackerStateReader
{
    SubmarineTrackerDataFingerprint GetDataFingerprint(EtaSettings settings);

    IReadOnlyList<FcState> Read(EtaSettings settings, ICollection<string> warnings);
}

public sealed class SubmarineTrackerStateReader : ISubmarineTrackerStateReader
{
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
            var fcs = ReadFreeCompanies(dbPath, warnings);
            var subs = ReadSubmarines(dbPath, settings, warnings)
                .GroupBy(s => Convert.ToHexString(s.FcId))
                .ToDictionary(g => g.Key, g => (IReadOnlyList<SubmarineState>)g.OrderBy(s => s.Name).ToArray());

            return fcs.Select(fc =>
            {
                subs.TryGetValue(fc.FcIdKey, out var fcSubs);
                return fc with { Submarines = fcSubs ?? [] };
            }).OrderBy(fc => fc.DisplayName).ToArray();
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

    private static IReadOnlyList<FcState> ReadFreeCompanies(string dbPath, ICollection<string> warnings)
    {
        using var connection = OpenReadOnly(dbPath);
        using var command = connection.CreateCommand();
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

    private static IReadOnlyList<SubmarineState> ReadSubmarines(string dbPath, EtaSettings settings, ICollection<string> warnings)
    {
        using var connection = OpenReadOnly(dbPath);
        using var command = connection.CreateCommand();
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

            subs.Add(new SubmarineState(
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
                manualOverride));
        }

        return subs;
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
}
