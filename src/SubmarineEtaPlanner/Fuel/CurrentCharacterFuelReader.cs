namespace SubmarineEtaPlanner.Fuel;

internal sealed class CurrentCharacterFuelReader : ICurrentCharacterFuelReader
{
    private const string ReadFailureWarning =
        "The current character's ceruleum tank count could not be read.";

    private readonly IGameFuelInventoryReader gameReader;
    private readonly Action<Exception, string> logError;

    public CurrentCharacterFuelReader(
        IGameFuelInventoryReader gameReader,
        Action<Exception, string> logError)
    {
        this.gameReader = gameReader ?? throw new ArgumentNullException(nameof(gameReader));
        this.logError = logError ?? throw new ArgumentNullException(nameof(logError));
    }

    public CharacterFuelObservation? TryRead(
        DateTimeOffset now,
        ICollection<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);

        try
        {
            var data = this.gameReader.TryRead();
            if (data is null || data.CharacterId == 0 || data.FreeCompanyId == 0)
                return null;

            return new CharacterFuelObservation(
                data.CharacterId,
                data.FreeCompanyId,
                data.CharacterName,
                data.World,
                data.CeruleumTanks,
                now.ToUniversalTime(),
                IsLive: true);
        }
        catch (Exception ex)
        {
            this.logError(ex, "Failed to read current character ceruleum stock.");
            warnings.Add(ReadFailureWarning);
            return null;
        }
    }
}
