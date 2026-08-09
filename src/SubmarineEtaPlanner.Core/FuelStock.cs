namespace SubmarineEtaPlanner;

public enum FuelStockSourceKind
{
    Manual = 0,
    LiveCharacter = 1,
    LastObservedCharacter = 2,
}

public sealed record CharacterFuelObservation(
    ulong CharacterId,
    ulong FreeCompanyId,
    string CharacterName,
    string World,
    int CeruleumTanks,
    DateTimeOffset ObservedAtUtc,
    bool IsLive);

public sealed record ResolvedFuelStock(
    int? CeruleumTanks,
    FuelStockSourceKind? Source,
    ulong? CharacterId,
    string? CharacterName,
    string? World,
    DateTimeOffset? ObservedAtUtc,
    bool IsLive,
    string? UnavailableReason)
{
    public bool IsAvailable => CeruleumTanks is not null && UnavailableReason is null;
}

public static class FuelStockResolver
{
    private const string SelectedCharacterUnavailableReason =
        "The selected fuel-holder character has not been observed in this FC.";

    private const string NoObservationUnavailableReason =
        "No character has been observed in this FC.";

    private const string MultipleCharactersUnavailableReason =
        "Multiple characters have been observed in this FC. Select the character that carries the workshop fuel.";

    public static ResolvedFuelStock Resolve(
        ulong freeCompanyId,
        FuelStockMode mode,
        ulong? selectedCharacterId,
        int manualCeruleumTanks,
        IReadOnlyList<CharacterFuelObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        return mode switch
        {
            FuelStockMode.Manual => AvailableManual(manualCeruleumTanks),
            FuelStockMode.Character => ResolveSelectedCharacter(
                freeCompanyId,
                selectedCharacterId,
                observations),
            FuelStockMode.Automatic => ResolveAutomatically(freeCompanyId, observations),
            _ => Unavailable("The configured fuel-stock mode is not supported."),
        };
    }

    private static ResolvedFuelStock ResolveSelectedCharacter(
        ulong freeCompanyId,
        ulong? selectedCharacterId,
        IReadOnlyList<CharacterFuelObservation> observations)
    {
        var observation = observations
            .Where(candidate =>
                candidate.FreeCompanyId == freeCompanyId &&
                candidate.CharacterId == selectedCharacterId)
            .OrderObservations()
            .FirstOrDefault();

        return observation is null
            ? Unavailable(SelectedCharacterUnavailableReason)
            : AvailableFromObservation(observation);
    }

    private static ResolvedFuelStock ResolveAutomatically(
        ulong freeCompanyId,
        IReadOnlyList<CharacterFuelObservation> observations)
    {
        var candidates = observations
            .Where(observation => observation.FreeCompanyId == freeCompanyId)
            .GroupBy(observation => observation.CharacterId)
            .Select(group => group.OrderObservations().First())
            .ToArray();

        var liveCandidate = candidates
            .Where(candidate => candidate.IsLive)
            .OrderObservations()
            .FirstOrDefault();

        if (liveCandidate is not null)
            return AvailableFromObservation(liveCandidate);

        return candidates.Length switch
        {
            0 => Unavailable(NoObservationUnavailableReason),
            1 => AvailableFromObservation(candidates[0]),
            _ => Unavailable(MultipleCharactersUnavailableReason),
        };
    }

    private static IOrderedEnumerable<CharacterFuelObservation> OrderObservations(
        this IEnumerable<CharacterFuelObservation> observations) =>
        observations
            .OrderByDescending(observation => observation.IsLive)
            .ThenByDescending(observation => observation.ObservedAtUtc.UtcTicks)
            .ThenBy(observation => observation.ObservedAtUtc.Offset)
            .ThenBy(observation => observation.CharacterId)
            .ThenBy(observation => observation.CharacterName, StringComparer.Ordinal)
            .ThenBy(observation => observation.World, StringComparer.Ordinal)
            .ThenByDescending(observation => observation.CeruleumTanks);

    private static ResolvedFuelStock AvailableManual(int ceruleumTanks) =>
        new(
            ceruleumTanks,
            FuelStockSourceKind.Manual,
            CharacterId: null,
            CharacterName: null,
            World: null,
            ObservedAtUtc: null,
            IsLive: true,
            UnavailableReason: null);

    private static ResolvedFuelStock AvailableFromObservation(CharacterFuelObservation observation) =>
        new(
            observation.CeruleumTanks,
            observation.IsLive
                ? FuelStockSourceKind.LiveCharacter
                : FuelStockSourceKind.LastObservedCharacter,
            observation.CharacterId,
            observation.CharacterName,
            observation.World,
            observation.ObservedAtUtc,
            observation.IsLive,
            UnavailableReason: null);

    private static ResolvedFuelStock Unavailable(string reason) =>
        new(
            CeruleumTanks: null,
            Source: null,
            CharacterId: null,
            CharacterName: null,
            World: null,
            ObservedAtUtc: null,
            IsLive: false,
            UnavailableReason: reason);
}
