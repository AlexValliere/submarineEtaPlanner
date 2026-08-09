namespace SubmarineEtaPlanner.Fuel;

internal sealed class FuelObservationCoordinator : IDisposable
{
    private static readonly TimeSpan DefaultPollingInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DefaultTimestampRefreshInterval = TimeSpan.FromMinutes(10);

    private readonly ICurrentCharacterFuelReader reader;
    private readonly IFuelObservationStore store;
    private readonly ICollection<string> warnings;
    private readonly TimeSpan pollingInterval;
    private readonly TimeSpan timestampRefreshInterval;
    private readonly Dictionary<ulong, DateTimeOffset> persistedTimestamps = [];

    private CharacterFuelObservation[] observations;
    private DateTimeOffset nextPollAtUtc = DateTimeOffset.MinValue;
    private bool hasPendingChanges;
    private bool disposed;

    public FuelObservationCoordinator(
        ICurrentCharacterFuelReader reader,
        IFuelObservationStore store,
        ICollection<string> warnings)
        : this(
            reader,
            store,
            warnings,
            DefaultPollingInterval,
            DefaultTimestampRefreshInterval)
    {
    }

    internal FuelObservationCoordinator(
        ICurrentCharacterFuelReader reader,
        IFuelObservationStore store,
        ICollection<string> warnings,
        TimeSpan pollingInterval,
        TimeSpan timestampRefreshInterval)
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));

        if (pollingInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollingInterval));
        if (timestampRefreshInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timestampRefreshInterval));

        this.pollingInterval = pollingInterval;
        this.timestampRefreshInterval = timestampRefreshInterval;
        this.observations = NormalizeLoaded(this.store.Load(this.warnings));
        foreach (var observation in this.observations)
            this.persistedTimestamps[observation.CharacterId] = observation.ObservedAtUtc;
    }

    public CharacterFuelObservation? LiveObservation { get; private set; }

    public IReadOnlyList<CharacterFuelObservation> Observations => this.observations;

    public bool ForgetObservation(ulong characterId)
    {
        if (this.disposed || characterId == 0 || this.observations.All(item => item.CharacterId != characterId))
            return false;

        this.observations = this.observations
            .Where(observation => observation.CharacterId != characterId)
            .ToArray();
        if (this.LiveObservation?.CharacterId == characterId)
            this.LiveObservation = null;
        this.persistedTimestamps.Remove(characterId);
        this.hasPendingChanges = true;
        SavePending();
        return true;
    }

    public void Tick(DateTimeOffset now)
    {
        if (this.disposed)
            return;

        var nowUtc = now.ToUniversalTime();
        if (nowUtc < this.nextPollAtUtc)
            return;

        this.nextPollAtUtc = nowUtc.Add(this.pollingInterval);
        var current = this.reader.TryRead(nowUtc, this.warnings);
        if (current is null)
        {
            ClearLiveObservation();
            return;
        }

        current = current with
        {
            ObservedAtUtc = nowUtc,
            IsLive = true,
        };

        var previousLiveCharacterId = this.LiveObservation?.CharacterId;
        var existing = this.observations.FirstOrDefault(
            observation => observation.CharacterId == current.CharacterId);
        var mustSaveImmediately =
            existing is null ||
            previousLiveCharacterId != current.CharacterId ||
            HasMaterialChange(existing, current);

        this.observations = this.observations
            .Where(observation => observation.CharacterId != current.CharacterId)
            .Select(observation => observation.IsLive ? observation with { IsLive = false } : observation)
            .Append(current)
            .OrderBy(observation => observation.CharacterId)
            .ToArray();
        this.LiveObservation = current;
        this.hasPendingChanges = true;

        var persistedTimestamp = this.persistedTimestamps.GetValueOrDefault(
            current.CharacterId,
            DateTimeOffset.MinValue);
        var timestampRefreshDue = nowUtc - persistedTimestamp >= this.timestampRefreshInterval;
        if (mustSaveImmediately || timestampRefreshDue)
            SavePending();
    }

    public void Dispose()
    {
        if (this.disposed)
            return;

        if (this.hasPendingChanges)
            SavePending();

        this.disposed = true;
    }

    private static CharacterFuelObservation[] NormalizeLoaded(
        IReadOnlyList<CharacterFuelObservation> loaded) =>
        loaded
            .Select((observation, index) => (Observation: observation, Index: index))
            .Where(item => item.Observation.CharacterId != 0)
            .GroupBy(item => item.Observation.CharacterId)
            .Select(group => group
                .OrderByDescending(item => item.Observation.ObservedAtUtc.UtcTicks)
                .ThenByDescending(item => item.Index)
                .First()
                .Observation)
            .Select(observation => observation with
                {
                    ObservedAtUtc = observation.ObservedAtUtc.ToUniversalTime(),
                    IsLive = false,
                })
            .OrderBy(observation => observation.CharacterId)
            .ToArray();

    private static bool HasMaterialChange(
        CharacterFuelObservation existing,
        CharacterFuelObservation current) =>
        existing.FreeCompanyId != current.FreeCompanyId ||
        existing.CeruleumTanks != current.CeruleumTanks ||
        !string.Equals(existing.CharacterName, current.CharacterName, StringComparison.Ordinal) ||
        !string.Equals(existing.World, current.World, StringComparison.Ordinal);

    private void ClearLiveObservation()
    {
        if (this.LiveObservation is null)
            return;

        this.observations = this.observations
            .Select(observation => observation.IsLive ? observation with { IsLive = false } : observation)
            .ToArray();
        this.LiveObservation = null;
    }

    private void SavePending()
    {
        this.store.Save(this.observations, this.warnings);
        foreach (var observation in this.observations)
            this.persistedTimestamps[observation.CharacterId] = observation.ObservedAtUtc;
        this.hasPendingChanges = false;
    }
}
