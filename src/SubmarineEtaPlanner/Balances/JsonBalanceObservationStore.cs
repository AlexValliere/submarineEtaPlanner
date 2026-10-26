using System.Text.Json;
using SubmarineEtaPlanner.Fuel;

namespace SubmarineEtaPlanner.Balances;

internal sealed class JsonBalanceObservationStore : IBalanceObservationStore
{
    internal const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
    };
    private readonly string filePath;
    private readonly IAtomicFileWriter atomicFileWriter;
    private readonly Func<DateTimeOffset> utcNow;
    private bool writesBlocked;

    public JsonBalanceObservationStore(string filePath)
        : this(filePath, new AtomicFileWriter(), () => DateTimeOffset.UtcNow) { }

    internal JsonBalanceObservationStore(string filePath, IAtomicFileWriter atomicFileWriter, Func<DateTimeOffset> utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        this.filePath = Path.GetFullPath(filePath);
        this.atomicFileWriter = atomicFileWriter;
        this.utcNow = utcNow;
    }

    public GilBalanceSnapshot Load(ICollection<string> warnings)
    {
        this.writesBlocked = false;
        if (!File.Exists(this.filePath)) return GilBalanceSnapshot.Empty;
        try
        {
            using var stream = new FileStream(this.filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var stored = JsonSerializer.Deserialize<StoredBalances>(stream, SerializerOptions)
                ?? throw new JsonException("The observation file did not contain an object.");
            if (stored.Version != CurrentVersion)
            {
                this.writesBlocked = true;
                Warn(warnings, $"Gil observations use unsupported version {stored.Version}; the file was left unchanged.");
                return GilBalanceSnapshot.Empty;
            }
            return GilBalanceCalculator.Normalize(new(
                (stored.Characters ?? []).Where(item => item is not null).ToArray(),
                (stored.Chests ?? []).Where(item => item is not null).ToArray()), clearLive: true);
        }
        catch (JsonException)
        {
            PreserveCorruptFile(warnings);
            Warn(warnings, "Saved gil observations could not be loaded. The corrupt file was preserved.");
            return GilBalanceSnapshot.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // A temporary inability to read is not proof that the file is corrupt.
            this.writesBlocked = true;
            Warn(warnings, "Saved gil observations could not be read; the existing file was left unchanged.");
            return GilBalanceSnapshot.Empty;
        }
    }

    public bool Save(GilBalanceSnapshot snapshot, ICollection<string> warnings)
    {
        if (this.writesBlocked) return false;
        var normalized = GilBalanceCalculator.Normalize(snapshot, clearLive: true);
        var stored = new StoredBalances
        {
            Characters = normalized.Characters.ToArray(),
            Chests = normalized.Chests.ToArray(),
        };
        try
        {
            this.atomicFileWriter.Write(this.filePath,
                stream => JsonSerializer.Serialize(stream, stored, SerializerOptions));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            Warn(warnings, "Gil observations could not be saved. The previous file was preserved.");
            return false;
        }
    }

    private void PreserveCorruptFile(ICollection<string> warnings)
    {
        var directory = Path.GetDirectoryName(this.filePath)!;
        var name = Path.GetFileNameWithoutExtension(this.filePath);
        var extension = Path.GetExtension(this.filePath);
        var timestamp = this.utcNow().ToUniversalTime().ToString("yyyyMMdd-HHmmss");
        var target = Path.Combine(directory, $"{name}.corrupt-{timestamp}{extension}");
        try
        {
            for (var suffix = 1; File.Exists(target); suffix++)
                target = Path.Combine(directory, $"{name}.corrupt-{timestamp}-{suffix}{extension}");
            File.Move(this.filePath, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.writesBlocked = true;
            Warn(warnings, "The corrupt gil observation file could not be renamed and will not be overwritten.");
        }
    }

    internal static void Warn(ICollection<string> warnings, string message)
    {
        if (!warnings.Contains(message)) warnings.Add(message);
    }

    private sealed class StoredBalances
    {
        public int Version { get; init; } = CurrentVersion;
        public CharacterGilObservation[]? Characters { get; init; } = [];
        public FcChestGilObservation[]? Chests { get; init; } = [];
    }
}
