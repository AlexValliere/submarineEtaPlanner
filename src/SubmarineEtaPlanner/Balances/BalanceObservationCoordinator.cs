namespace SubmarineEtaPlanner.Balances;

internal sealed class BalanceObservationCoordinator : IDisposable
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TimestampCheckpointInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FailedSaveRetryInterval = TimeSpan.FromSeconds(30);
    private readonly ICurrentBalanceReader reader;
    private readonly IBalanceObservationStore store;
    private readonly ICollection<string> warnings;
    private DateTimeOffset nextPollAtUtc = DateTimeOffset.MinValue;
    private DateTimeOffset lastSavedAtUtc = DateTimeOffset.MinValue;
    private DateTimeOffset nextSaveAttemptAtUtc = DateTimeOffset.MinValue;
    private bool pending;
    private bool disposed;

    public BalanceObservationCoordinator(ICurrentBalanceReader reader, IBalanceObservationStore store,
        ICollection<string> warnings)
    {
        this.reader = reader;
        this.store = store;
        this.warnings = warnings;
        Snapshot = GilBalanceCalculator.Normalize(store.Load(warnings), clearLive: true);
    }

    public GilBalanceSnapshot Snapshot { get; private set; }

    public void Tick(DateTimeOffset now)
    {
        if (this.disposed || now < this.nextPollAtUtc) return;
        now = now.ToUniversalTime();
        this.nextPollAtUtc = now.Add(PollingInterval);
        BalanceReadResult? read;
        try { read = this.reader.TryRead(now, this.warnings); }
        catch (Exception)
        {
            JsonBalanceObservationStore.Warn(this.warnings, "The current gil balances could not be read.");
            read = null;
        }

        if (read is null || read.Character.CharacterId == 0 || read.Character.Gil is < 0 or > uint.MaxValue)
        {
            if (Snapshot.Characters.Any(character => character.IsLive))
                Snapshot = Snapshot with { Characters = Snapshot.Characters.Select(character => character with { IsLive = false }).ToArray() };
            SavePending(now);
            return;
        }

        var previous = Snapshot.Characters.FirstOrDefault(character => character.CharacterId == read.Character.CharacterId);
        var current = read.Character with
        {
            // A visiting character or a temporarily unloaded FC proxy does not prove an FC change.
            FreeCompanyId = read.Character.FreeCompanyId ?? previous?.FreeCompanyId,
            ObservedAtUtc = now,
            IsLive = true,
        };
        var materialChange = previous is null || previous.Gil != current.Gil ||
            previous.FreeCompanyId != current.FreeCompanyId ||
            previous.CharacterName != current.CharacterName || previous.World != current.World ||
            !previous.IsLive;
        var characters = Snapshot.Characters.Where(character => character.CharacterId != current.CharacterId)
            .Select(character => character.IsLive ? character with { IsLive = false } : character)
            .Append(current).OrderBy(character => character.CharacterId).ToArray();
        var chests = Snapshot.Chests;
        if (read.Chest is { } chest && chest.FreeCompanyId != 0 &&
            chest.FreeCompanyId == read.Character.FreeCompanyId && chest.Gil is >= 0 and <= uint.MaxValue)
        {
            var previousChest = chests.FirstOrDefault(candidate => candidate.FreeCompanyId == chest.FreeCompanyId);
            materialChange |= previousChest is null || previousChest.Gil != chest.Gil;
            chests = chests.Where(candidate => candidate.FreeCompanyId != chest.FreeCompanyId)
                .Append(chest with { ObservedAtUtc = now }).OrderBy(candidate => candidate.FreeCompanyId).ToArray();
        }
        Snapshot = new(characters, chests);
        this.pending = true;
        if (materialChange || now - this.lastSavedAtUtc >= TimestampCheckpointInterval ||
            this.nextSaveAttemptAtUtc != DateTimeOffset.MinValue)
            SavePending(now);
    }

    public bool ForgetCharacter(ulong characterId)
    {
        if (this.disposed || characterId == 0 || Snapshot.Characters.All(character => character.CharacterId != characterId))
            return false;
        Snapshot = Snapshot with { Characters = Snapshot.Characters.Where(character => character.CharacterId != characterId).ToArray() };
        this.pending = true;
        SavePending(DateTimeOffset.UtcNow, force: true);
        return true;
    }

    public void Dispose()
    {
        if (this.disposed) return;
        SavePending(DateTimeOffset.UtcNow, force: true);
        this.disposed = true;
    }

    private void SavePending(DateTimeOffset now, bool force = false)
    {
        if (!this.pending || (!force && now < this.nextSaveAttemptAtUtc)) return;
        if (this.store.Save(Snapshot, this.warnings))
        {
            this.pending = false;
            this.lastSavedAtUtc = now;
            this.nextSaveAttemptAtUtc = DateTimeOffset.MinValue;
        }
        else this.nextSaveAttemptAtUtc = now.Add(FailedSaveRetryInterval);
    }
}
