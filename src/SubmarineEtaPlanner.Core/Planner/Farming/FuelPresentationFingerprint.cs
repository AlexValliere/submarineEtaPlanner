namespace SubmarineEtaPlanner.Planner;

internal static class FuelPresentationFingerprint
{
    public static string Create(FcState fc, FcPreferences preferences, int target, int delay, ResolvedFuelStock stock)
        => FcDataFingerprint.Hash(writer =>
        {
            writer.Write((fc.DataFingerprint.IsEmpty ? FcDataFingerprint.Create(fc) : fc.DataFingerprint).Value);
            writer.Write(fc.GameFreeCompanyId ?? 0);
            writer.Write(target);
            writer.Write(delay);
            writer.Write(preferences.CeruleumReserve ?? -1);
            writer.Write((int)preferences.FuelStockMode);
            writer.Write(preferences.FuelHolderCharacterId ?? 0);
            writer.Write(preferences.ManualCeruleumTanks ?? 0);
            foreach (var (id, sub) in preferences.Submarines.OrderBy(pair => pair.Key))
            {
                writer.Write(id);
                writer.Write((int)sub.Assignment);
                writer.Write(sub.CollectionDelayMinutes ?? -1);
                writer.Write(sub.PinnedFarmingRoute?.Count ?? 0);
                foreach (var sector in sub.PinnedFarmingRoute ?? []) writer.Write(sector);
            }
            writer.Write(stock.CeruleumTanks ?? -1);
            writer.Write(stock.Source is { } source ? (int)source : -1);
            writer.Write(stock.CharacterId ?? 0);
            writer.Write(stock.CharacterName ?? string.Empty);
            writer.Write(stock.World ?? string.Empty);
            writer.Write(stock.ObservedAtUtc?.UtcTicks ?? 0);
            writer.Write(stock.IsLive);
            writer.Write(stock.UnavailableReason ?? string.Empty);
        });
}
