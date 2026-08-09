using System.Globalization;

namespace SubmarineEtaPlanner;

public sealed record FuelStockSourcePresentation(
    string ResultLine,
    string? DetailLine,
    string SourceLine);

public static class FuelStockPresentation
{
    public static IReadOnlyList<CharacterFuelObservation> CandidatesForFreeCompany(
        ulong? freeCompanyId,
        IReadOnlyList<CharacterFuelObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (freeCompanyId is null)
            return [];

        return observations
            .Select((observation, index) => (Observation: observation, Index: index))
            .Where(item => item.Observation.FreeCompanyId == freeCompanyId.Value)
            .GroupBy(item => item.Observation.CharacterId)
            .Select(group => group
                .OrderByDescending(item => item.Observation.IsLive)
                .ThenByDescending(item => item.Observation.ObservedAtUtc.UtcTicks)
                .ThenByDescending(item => item.Index)
                .First()
                .Observation)
            .OrderByDescending(observation => observation.IsLive)
            .ThenBy(observation => observation.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(observation => observation.World, StringComparer.OrdinalIgnoreCase)
            .ThenBy(observation => observation.CharacterId)
            .ToArray();
    }

    public static string FormatCandidate(CharacterFuelObservation observation, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var freshness = observation.IsLive
            ? "Live"
            : $"Last observed {FormatAgeCompact(observation.ObservedAtUtc, now)} ago";
        return $"{CharacterLabel(observation)} — {FormatTanks(observation.CeruleumTanks)} tanks — {freshness}";
    }

    public static FuelStockSourcePresentation Describe(ResolvedFuelStock stock, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(stock);
        if (!stock.IsAvailable || stock.CeruleumTanks is not { } tanks || stock.Source is null)
        {
            var unavailable = stock.UnavailableReason ?? "Ceruleum stock is unavailable.";
            return new FuelStockSourcePresentation(unavailable, null, "Unavailable");
        }

        if (stock.Source == FuelStockSourceKind.Manual)
        {
            return new FuelStockSourcePresentation(
                $"Manual — {FormatTanks(tanks)} tanks",
                null,
                "Manual");
        }

        var character = CharacterLabel(stock.CharacterName, stock.World);
        if (stock.Source == FuelStockSourceKind.LiveCharacter)
        {
            return new FuelStockSourcePresentation(
                $"Live — {character} — {FormatTanks(tanks)} tanks",
                null,
                $"{character} — Live");
        }

        var observedAt = stock.ObservedAtUtc;
        var compactAge = observedAt is null ? null : FormatAgeCompact(observedAt.Value, now);
        return new FuelStockSourcePresentation(
            $"Last observed — {character} — {FormatTanks(tanks)} tanks",
            observedAt is null ? null : $"Observed {FormatAgeLong(observedAt.Value, now)} ago",
            compactAge is null
                ? $"{character} — Last observed"
                : $"{character} — Last observed {compactAge} ago");
    }

    public static string CharacterLabel(CharacterFuelObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return CharacterLabel(observation.CharacterName, observation.World);
    }

    private static string CharacterLabel(string? characterName, string? world)
    {
        var name = string.IsNullOrWhiteSpace(characterName) ? "Unknown character" : characterName;
        var worldName = string.IsNullOrWhiteSpace(world) ? "Unknown world" : world;
        return $"{name}@{worldName}";
    }

    private static string FormatTanks(int tanks) =>
        tanks.ToString("N0", CultureInfo.InvariantCulture);

    private static string FormatAgeCompact(DateTimeOffset observedAt, DateTimeOffset now)
    {
        var elapsed = Elapsed(observedAt, now);
        if (elapsed.TotalDays >= 1)
            return $"{Math.Max(1, (int)elapsed.TotalDays)}d";
        if (elapsed.TotalHours >= 1)
            return $"{Math.Max(1, (int)elapsed.TotalHours)}h";
        return $"{Math.Max(1, (int)elapsed.TotalMinutes)}m";
    }

    private static string FormatAgeLong(DateTimeOffset observedAt, DateTimeOffset now)
    {
        var elapsed = Elapsed(observedAt, now);
        if (elapsed.TotalDays >= 1)
        {
            var days = Math.Max(1, (int)elapsed.TotalDays);
            return $"{days} {(days == 1 ? "day" : "days")}";
        }
        if (elapsed.TotalHours >= 1)
        {
            var hours = Math.Max(1, (int)elapsed.TotalHours);
            return $"{hours} {(hours == 1 ? "hour" : "hours")}";
        }

        var minutes = Math.Max(1, (int)elapsed.TotalMinutes);
        return $"{minutes} {(minutes == 1 ? "minute" : "minutes")}";
    }

    private static TimeSpan Elapsed(DateTimeOffset observedAt, DateTimeOffset now)
    {
        var elapsed = now.ToUniversalTime() - observedAt.ToUniversalTime();
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }
}
