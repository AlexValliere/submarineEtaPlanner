namespace SubmarineEtaPlanner.Fuel;

internal interface ICurrentCharacterFuelReader
{
    CharacterFuelObservation? TryRead(
        DateTimeOffset now,
        ICollection<string> warnings);
}

internal interface IGameFuelInventoryReader
{
    CurrentCharacterFuelData? TryRead();
}

internal sealed record CurrentCharacterFuelData(
    ulong CharacterId,
    ulong FreeCompanyId,
    string CharacterName,
    string World,
    int CeruleumTanks);
