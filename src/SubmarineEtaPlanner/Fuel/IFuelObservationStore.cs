namespace SubmarineEtaPlanner.Fuel;

internal interface IFuelObservationStore
{
    IReadOnlyList<CharacterFuelObservation> Load(ICollection<string> warnings);

    void Save(
        IReadOnlyList<CharacterFuelObservation> observations,
        ICollection<string> warnings);
}

internal sealed record StoredFuelObservationFile
{
    public int Version { get; init; } = 1;

    public List<StoredCharacterFuelObservation> Characters { get; init; } = [];
}

internal sealed record StoredCharacterFuelObservation
{
    public ulong CharacterId { get; init; }

    public ulong FreeCompanyId { get; init; }

    public string CharacterName { get; init; } = string.Empty;

    public string World { get; init; } = string.Empty;

    public int CeruleumTanks { get; init; }

    public DateTimeOffset ObservedAtUtc { get; init; }
}
