namespace SubmarineEtaPlanner.Balances;

internal interface ICurrentBalanceReader
{
    BalanceReadResult? TryRead(DateTimeOffset now, ICollection<string> warnings);
}

internal sealed record BalanceReadResult(CharacterGilObservation Character, FcChestGilObservation? Chest);

internal interface IBalanceObservationStore
{
    GilBalanceSnapshot Load(ICollection<string> warnings);
    bool Save(GilBalanceSnapshot snapshot, ICollection<string> warnings);
}
