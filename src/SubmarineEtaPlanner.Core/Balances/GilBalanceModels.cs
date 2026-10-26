namespace SubmarineEtaPlanner;

public sealed record CharacterGilObservation(
    ulong CharacterId,
    ulong? FreeCompanyId,
    string CharacterName,
    string World,
    long Gil,
    DateTimeOffset ObservedAtUtc,
    bool IsLive);

public sealed record FcChestGilObservation(
    ulong FreeCompanyId,
    long Gil,
    DateTimeOffset ObservedAtUtc);

public sealed record GilBalanceSnapshot(
    IReadOnlyList<CharacterGilObservation> Characters,
    IReadOnlyList<FcChestGilObservation> Chests)
{
    public static GilBalanceSnapshot Empty { get; } = new([], []);
}

public sealed record FcGilBalance(
    ulong? FreeCompanyId,
    IReadOnlyList<CharacterGilObservation> Characters,
    FcChestGilObservation? Chest)
{
    public long? WalletGil => Characters.Count == 0 ? null : Characters.Sum(character => character.Gil);
    public long? ChestGil => Chest?.Gil;
    public long? RecordedGil => HasAnyObservation ? (WalletGil ?? 0) + (ChestGil ?? 0) : null;
    public bool HasAnyObservation => Characters.Count > 0 || Chest is not null;
    public DateTimeOffset? OldestWalletObservationAtUtc => Characters.Count == 0
        ? null : Characters.Min(character => character.ObservedAtUtc);
}

public static class GilBalanceCalculator
{
    public static FcGilBalance ForFreeCompany(ulong? freeCompanyId, GilBalanceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (freeCompanyId is null or 0)
            return new(freeCompanyId, [], null);

        return new(
            freeCompanyId,
            NormalizeCharacters(snapshot.Characters)
                .Where(character => character.FreeCompanyId == freeCompanyId)
                .OrderBy(character => character.CharacterName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(character => character.World, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            NormalizeChests(snapshot.Chests).FirstOrDefault(chest => chest.FreeCompanyId == freeCompanyId));
    }

    public static IReadOnlyList<CharacterGilObservation> UnassignedCharacters(GilBalanceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return NormalizeCharacters(snapshot.Characters)
            .Where(character => character.FreeCompanyId is null or 0)
            .OrderBy(character => character.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(character => character.World, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static GilBalanceSnapshot Normalize(GilBalanceSnapshot snapshot, bool clearLive = false)
        => new(NormalizeCharacters(snapshot.Characters)
            .Select(character => character with
            {
                ObservedAtUtc = character.ObservedAtUtc.ToUniversalTime(),
                IsLive = !clearLive && character.IsLive,
            }).ToArray(),
            NormalizeChests(snapshot.Chests)
                .Select(chest => chest with { ObservedAtUtc = chest.ObservedAtUtc.ToUniversalTime() })
                .ToArray());

    private static IEnumerable<CharacterGilObservation> NormalizeCharacters(IEnumerable<CharacterGilObservation> observations)
        => observations
            .Where(character => character.CharacterId != 0 && character.Gil is >= 0 and <= uint.MaxValue)
            .Select((character, index) => (Character: character, Index: index))
            .GroupBy(item => item.Character.CharacterId)
            .Select(group => group.OrderByDescending(item => item.Character.ObservedAtUtc)
                .ThenByDescending(item => item.Index).First().Character)
            .OrderBy(character => character.CharacterId);

    private static IEnumerable<FcChestGilObservation> NormalizeChests(IEnumerable<FcChestGilObservation> observations)
        => observations
            .Where(chest => chest.FreeCompanyId != 0 && chest.Gil is >= 0 and <= uint.MaxValue)
            .Select((chest, index) => (Chest: chest, Index: index))
            .GroupBy(item => item.Chest.FreeCompanyId)
            .Select(group => group.OrderByDescending(item => item.Chest.ObservedAtUtc)
                .ThenByDescending(item => item.Index).First().Chest)
            .OrderBy(chest => chest.FreeCompanyId);
}
