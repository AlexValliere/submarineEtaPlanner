using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class ForecastFingerprintTests
{
    [Fact]
    public void FcFingerprintIsStableAcrossSetAndSubmarineOrdering()
    {
        var first = CreateFc(
            unlocked: new HashSet<uint> { 3, 1, 2 },
            explored: new HashSet<uint> { 8, 5 },
            submarines: [CreateSubmarine(2, "Beta"), CreateSubmarine(1, "Alpha")]);
        var second = CreateFc(
            unlocked: new HashSet<uint> { 2, 3, 1 },
            explored: new HashSet<uint> { 5, 8 },
            submarines: [CreateSubmarine(1, "Alpha"), CreateSubmarine(2, "Beta")]);

        Assert.Equal(FcDataFingerprint.Create(first), FcDataFingerprint.Create(second));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("rank")]
    [InlineData("exp")]
    [InlineData("build")]
    [InlineData("return")]
    [InlineData("route")]
    [InlineData("override")]
    [InlineData("unlocked")]
    [InlineData("explored")]
    [InlineData("world")]
    [InlineData("known")]
    public void FcFingerprintChangesForCalculationRelevantData(string change)
    {
        var original = CreateFc();
        var sub = original.Submarines[0];
        var changed = change switch
        {
            "name" => original with { Submarines = [sub with { Name = "Changed" }] },
            "rank" => original with { Submarines = [sub with { Rank = sub.Rank + 1 }] },
            "exp" => original with { Submarines = [sub with { CurrentExp = sub.CurrentExp + 1 }] },
            "build" => original with { Submarines = [sub with { BuildParts = sub.BuildParts with { Hull = 9 } }] },
            "return" => original with { Submarines = [sub with { ReturnAtUtc = sub.ReturnAtUtc.AddMinutes(1) }] },
            "route" => original with { Submarines = [sub with { CurrentRoute = [1, 3] }] },
            "override" => original with { Submarines = [sub with { ManualCurrentRouteOverride = [4] }] },
            "unlocked" => original with { UnlockedPoints = new HashSet<uint> { 1, 2, 3 } },
            "explored" => original with { ExploredPoints = new HashSet<uint> { 1, 2 } },
            "world" => original with { World = "Ragnarok" },
            "known" => original with { UnlockDataKnown = false },
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };

        Assert.NotEqual(FcDataFingerprint.Create(original), FcDataFingerprint.Create(changed));
    }

    [Fact]
    public void SettingsFingerprintIgnoresDisplayOnlySettingsButTracksCalculationSettings()
    {
        var settings = EtaSettings.CreateDefault();
        var original = CalculationSettingsFingerprint.Create(settings);

        settings.ShowRouteDiagnostics = !settings.ShowRouteDiagnostics;
        settings.TimeoutResultBehavior = TimeoutResultBehavior.ShowPartial;
        Assert.Equal(original, CalculationSettingsFingerprint.Create(settings));

        settings.UnlockSuccessProbability = 0.5;
        Assert.NotEqual(original, CalculationSettingsFingerprint.Create(settings));
    }

    private static FcState CreateFc(
        IReadOnlySet<uint>? unlocked = null,
        IReadOnlySet<uint>? explored = null,
        IReadOnlyList<SubmarineState>? submarines = null)
        => new(
            [1],
            "TEST",
            "Cerberus",
            unlocked ?? new HashSet<uint> { 1, 2 },
            explored ?? new HashSet<uint> { 1 },
            submarines ?? [CreateSubmarine(1, "Alpha")]);

    private static SubmarineState CreateSubmarine(long id, string name)
        => new(
            [1],
            id,
            name,
            50,
            123,
            456,
            new SubmarineBuildParts(1, 2, 3, 4),
            DateTimeOffset.UnixEpoch.AddDays(10),
            [1, 2],
            true,
            []);
}
