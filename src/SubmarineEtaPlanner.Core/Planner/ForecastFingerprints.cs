using System.Security.Cryptography;
using System.Text;

namespace SubmarineEtaPlanner.Planner;

public readonly record struct FcDataFingerprint(string Value)
{
    public static FcDataFingerprint Create(FcState fc)
        => new(Hash(writer =>
        {
            writer.Write(fc.FcId.Length);
            writer.Write(fc.FcId);
            writer.Write(fc.FreeCompanyTag ?? string.Empty);
            writer.Write(fc.World ?? string.Empty);
            writer.Write(fc.UnlockDataKnown);
            WritePoints(writer, fc.UnlockedPoints);
            WritePoints(writer, fc.ExploredPoints);

            var submarines = fc.Submarines.OrderBy(submarine => submarine.SubmarineId).ToArray();
            writer.Write(submarines.Length);
            foreach (var submarine in submarines)
            {
                writer.Write(submarine.SubmarineId);
                writer.Write(submarine.Name ?? string.Empty);
                writer.Write(submarine.Rank);
                writer.Write(submarine.CurrentExp);
                writer.Write(submarine.NextLevelExp);
                writer.Write(submarine.BuildParts.Hull);
                writer.Write(submarine.BuildParts.Stern);
                writer.Write(submarine.BuildParts.Bow);
                writer.Write(submarine.BuildParts.Bridge);
                writer.Write(submarine.ReturnAtUtc.UtcTicks);
                writer.Write(submarine.CurrentVoyageKnown);
                WritePointsInOrder(writer, submarine.CurrentRoute);
                WritePointsInOrder(writer, submarine.ManualCurrentRouteOverride);
            }
        }));

    public bool IsEmpty => string.IsNullOrEmpty(Value);

    private static void WritePoints(BinaryWriter writer, IEnumerable<uint> points)
        => WritePointsInOrder(writer, points.OrderBy(point => point));

    private static void WritePointsInOrder(BinaryWriter writer, IEnumerable<uint> points)
    {
        var values = points.ToArray();
        writer.Write(values.Length);
        foreach (var point in values)
            writer.Write(point);
    }

    internal static string Hash(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            write(writer);
        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }
}

public readonly record struct CalculationSettingsFingerprint(string Value)
{
    public static CalculationSettingsFingerprint Create(EtaSettings settings)
        => new(FcDataFingerprint.Hash(writer =>
        {
            writer.Write(settings.TargetRank);
            writer.Write((int)settings.ExpMode);
            writer.Write(settings.CollectionDelayMinutes);
            writer.Write((int)settings.SimulationMode);
            writer.Write(settings.PrioritizeSubSlots);
            writer.Write((int)settings.RouteGoal);
            writer.Write(settings.DurationLimitHours);
            writer.Write((int)settings.EtaModel);
            writer.Write(settings.PracticalMaxVoyageHours);
            writer.Write(settings.OptimizeExpPerHour);
            writer.Write((int)settings.UnknownCurrentVoyagePolicy);
            writer.Write(settings.MaxPreviewVoyagesPerSubmarine);
            writer.Write(settings.SimulationSafetyVoyageCapPerSubmarine);
            writer.Write(settings.CalculationTimeLimitSeconds);
            writer.Write(settings.UnlockSuccessProbability);

            writer.Write(settings.BuildProfile.Count);
            foreach (var step in settings.BuildProfile)
            {
                writer.Write(step.MinRank);
                writer.Write(step.MaxRank);
                writer.Write(step.BuildCode ?? string.Empty);
            }

            var overrides = settings.ManualCurrentRouteOverrides.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
            writer.Write(overrides.Length);
            foreach (var (key, route) in overrides)
            {
                writer.Write(key);
                writer.Write(route.Count);
                foreach (var point in route)
                    writer.Write(point);
            }
        }));
}
