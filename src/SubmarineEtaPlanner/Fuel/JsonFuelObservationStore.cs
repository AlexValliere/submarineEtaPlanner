using System.Text.Json;

namespace SubmarineEtaPlanner.Fuel;

internal sealed class JsonFuelObservationStore : IFuelObservationStore
{
    internal const int CurrentVersion = 1;

    private const string LoadFailureWarning =
        "The saved workshop fuel observations could not be loaded. The corrupt file was preserved.";

    private const string SaveFailureWarning =
        "The workshop fuel observations could not be saved. The previous file was preserved.";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string filePath;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly IAtomicFileWriter atomicFileWriter;
    private int? unsupportedLoadedVersion;

    public JsonFuelObservationStore(string filePath)
        : this(filePath, () => DateTimeOffset.UtcNow, new AtomicFileWriter())
    {
    }

    internal JsonFuelObservationStore(
        string filePath,
        Func<DateTimeOffset> utcNow,
        IAtomicFileWriter atomicFileWriter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        this.filePath = Path.GetFullPath(filePath);
        this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        this.atomicFileWriter = atomicFileWriter ?? throw new ArgumentNullException(nameof(atomicFileWriter));
    }

    public IReadOnlyList<CharacterFuelObservation> Load(ICollection<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        this.unsupportedLoadedVersion = null;

        if (!File.Exists(this.filePath))
            return [];

        try
        {
            using var stream = new FileStream(
                this.filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            var stored = JsonSerializer.Deserialize<StoredFuelObservationFile>(stream, SerializerOptions)
                ?? throw new JsonException("The observation file did not contain a JSON object.");

            if (stored.Version > CurrentVersion)
            {
                this.unsupportedLoadedVersion = stored.Version;
                warnings.Add(
                    $"Workshop fuel observations use unsupported future version {stored.Version}; " +
                    "the file was left unchanged.");
                return [];
            }

            if (stored.Version != CurrentVersion)
            {
                this.unsupportedLoadedVersion = stored.Version;
                warnings.Add(
                    $"Workshop fuel observations use unsupported version {stored.Version}; " +
                    "the file was left unchanged.");
                return [];
            }

            return Normalize(stored.Characters ?? []).ToArray();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            PreserveCorruptFile(warnings);
            warnings.Add(LoadFailureWarning);
            return [];
        }
    }

    public void Save(
        IReadOnlyList<CharacterFuelObservation> observations,
        ICollection<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(warnings);

        if (this.unsupportedLoadedVersion is { } unsupportedVersion)
        {
            warnings.Add(
                $"Workshop fuel observations were not saved because the existing file uses unsupported version {unsupportedVersion}.");
            return;
        }

        var stored = new StoredFuelObservationFile
        {
            Version = CurrentVersion,
            Characters = Normalize(observations)
                .Select(observation => new StoredCharacterFuelObservation
                {
                    CharacterId = observation.CharacterId,
                    FreeCompanyId = observation.FreeCompanyId,
                    CharacterName = observation.CharacterName,
                    World = observation.World,
                    CeruleumTanks = observation.CeruleumTanks,
                    ObservedAtUtc = observation.ObservedAtUtc.ToUniversalTime(),
                })
                .ToList(),
        };

        try
        {
            this.atomicFileWriter.Write(
                this.filePath,
                stream => JsonSerializer.Serialize(stream, stored, SerializerOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            warnings.Add(SaveFailureWarning);
        }
    }

    private static IEnumerable<CharacterFuelObservation> Normalize(
        IEnumerable<StoredCharacterFuelObservation> observations) =>
        observations
            .Select((observation, index) => (Observation: observation, Index: index))
            .Where(item => item.Observation.CharacterId != 0)
            .GroupBy(item => item.Observation.CharacterId)
            .Select(group => group
                .OrderByDescending(item => item.Observation.ObservedAtUtc.UtcTicks)
                .ThenByDescending(item => item.Index)
                .First()
                .Observation)
            .OrderBy(observation => observation.CharacterId)
            .Select(observation => new CharacterFuelObservation(
                observation.CharacterId,
                observation.FreeCompanyId,
                observation.CharacterName,
                observation.World,
                observation.CeruleumTanks,
                observation.ObservedAtUtc.ToUniversalTime(),
                IsLive: false));

    private static IEnumerable<CharacterFuelObservation> Normalize(
        IEnumerable<CharacterFuelObservation> observations) =>
        observations
            .Select((observation, index) => (Observation: observation, Index: index))
            .Where(item => item.Observation.CharacterId != 0)
            .GroupBy(item => item.Observation.CharacterId)
            .Select(group => group
                .OrderByDescending(item => item.Observation.IsLive)
                .ThenByDescending(item => item.Observation.ObservedAtUtc.UtcTicks)
                .ThenByDescending(item => item.Index)
                .First()
                .Observation)
            .OrderBy(observation => observation.CharacterId);

    private void PreserveCorruptFile(ICollection<string> warnings)
    {
        var directory = Path.GetDirectoryName(this.filePath)!;
        var fileName = Path.GetFileNameWithoutExtension(this.filePath);
        var extension = Path.GetExtension(this.filePath);
        var timestamp = this.utcNow().ToUniversalTime().ToString("yyyyMMdd-HHmmss");
        var corruptPath = Path.Combine(directory, $"{fileName}.corrupt-{timestamp}{extension}");

        try
        {
            for (var suffix = 1; File.Exists(corruptPath); suffix++)
                corruptPath = Path.Combine(directory, $"{fileName}.corrupt-{timestamp}-{suffix}{extension}");

            File.Move(this.filePath, corruptPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add("The corrupt workshop fuel observation file could not be renamed, but it was not overwritten.");
        }
    }
}

internal interface IAtomicFileWriter
{
    void Write(string destinationPath, Action<Stream> writeContent);
}

internal sealed class AtomicFileWriter : IAtomicFileWriter
{
    private readonly Func<string, bool> fileExists;
    private readonly Action<string, string, string?> replaceFile;
    private readonly Action<string, string, bool> moveFile;

    public AtomicFileWriter()
        : this(File.Exists, File.Replace, File.Move)
    {
    }

    internal AtomicFileWriter(
        Func<string, bool> fileExists,
        Action<string, string, string?> replaceFile,
        Action<string, string, bool> moveFile)
    {
        this.fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
        this.replaceFile = replaceFile ?? throw new ArgumentNullException(nameof(replaceFile));
        this.moveFile = moveFile ?? throw new ArgumentNullException(nameof(moveFile));
    }

    public void Write(string destinationPath, Action<Stream> writeContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(writeContent);

        var directory = Path.GetDirectoryName(destinationPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                writeContent(stream);
                stream.Flush(flushToDisk: true);
            }

            if (!this.fileExists(destinationPath))
            {
                this.moveFile(temporaryPath, destinationPath, false);
                return;
            }

            try
            {
                this.replaceFile(temporaryPath, destinationPath, null);
            }
            catch (Exception ex) when (ex is IOException or PlatformNotSupportedException)
            {
                this.moveFile(temporaryPath, destinationPath, true);
            }
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // A leftover temporary file is safer than touching the valid destination.
            }
            catch (UnauthorizedAccessException)
            {
                // A leftover temporary file is safer than touching the valid destination.
            }
        }
    }
}
