using SubmarineEtaPlanner.Balances;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class BalanceAssociationDiagnosticsTests
{
    private static readonly BalanceAssociationDiagnostics Ready = new(
        HomeWorldId: 67,
        CurrentWorldId: 67,
        CharacterHomeWorldId: 67,
        CharacterCurrentWorldId: 67,
        HasFreeCompanyTag: true,
        ProxyFreeCompanyId: 9235199887244466359,
        ProxyHomeWorldId: 67,
        HasFreeCompanyName: true,
        MemberProxyFreeCompanyId: 9235199887244466359);

    [Theory]
    [InlineData(null, 67U, 67U)]
    [InlineData(66U, 67U, 67U)]
    [InlineData(67U, 66U, 67U)]
    [InlineData(67U, 67U, 66U)]
    public void MissingOrMismatchedWorldsLeaveTheAssociationUnknown(
        uint? currentWorldId, uint characterHomeWorldId, uint characterCurrentWorldId)
    {
        var diagnostics = Ready with
        {
            CurrentWorldId = currentWorldId,
            CharacterHomeWorldId = characterHomeWorldId,
            CharacterCurrentWorldId = characterCurrentWorldId,
        };

        var result = diagnostics.Resolve();

        Assert.Null(result.FreeCompanyId);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        Assert.Null((diagnostics with { HasFreeCompanyTag = false, ProxyFreeCompanyId = 0 })
            .Resolve().FreeCompanyId);
    }

    [Fact]
    public void MissingProxyDoesNotBecomeAConfirmedAbsence()
    {
        var diagnostics = Ready with { ProxyFreeCompanyId = null, HasFreeCompanyTag = false };

        Assert.Null(diagnostics.Resolve().FreeCompanyId);
    }

    [Fact]
    public void AVisibleTagWithAnEmptyProxyIdLeavesTheAssociationUnknown()
    {
        var diagnostics = Ready with { ProxyFreeCompanyId = 0 };

        Assert.Null(diagnostics.Resolve().FreeCompanyId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AReadyFcIdIsAcceptedRegardlessOfProxyNameAvailability(bool hasProxyName)
    {
        var diagnostics = Ready with { HasFreeCompanyName = hasProxyName };

        Assert.Equal(Ready.ProxyFreeCompanyId, diagnostics.Resolve().FreeCompanyId);
    }

    [Fact]
    public void AProxyIdWithoutTheCharactersTagDoesNotConfirmMembership()
    {
        var diagnostics = Ready with { HasFreeCompanyTag = false };

        Assert.Null(diagnostics.Resolve().FreeCompanyId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyTagAndZeroProxyIdKeepTheExistingConfirmedAbsenceBehavior(bool hasProxyName)
    {
        var diagnostics = Ready with
        {
            HasFreeCompanyTag = false,
            ProxyFreeCompanyId = 0,
            HasFreeCompanyName = hasProxyName,
        };

        Assert.Equal(0UL, diagnostics.Resolve().FreeCompanyId);
    }

    [Fact]
    public void ReadyAssociationUsesTheNumericProxyId()
    {
        Assert.Equal(Ready.ProxyFreeCompanyId, Ready.Resolve().FreeCompanyId);
    }

    [Fact]
    public void AdditionalProxyWorldAndMemberInformationDoesNotChangeExistingDecisions()
    {
        var diagnostics = Ready with
        {
            ProxyHomeWorldId = 999,
            MemberProxyFreeCompanyId = 9876543210123456789,
        };

        Assert.Equal(Ready.ProxyFreeCompanyId, diagnostics.Resolve().FreeCompanyId);
        diagnostics = diagnostics with { ProxyHomeWorldId = null, MemberProxyFreeCompanyId = null };
        Assert.Equal(Ready.ProxyFreeCompanyId, diagnostics.Resolve().FreeCompanyId);
    }

    [Fact]
    public void FormattedDiagnosticsIncludeEveryNumericSourceAndTheRecordedAssociation()
    {
        var diagnostics = Ready with
        {
            HomeWorldId = 401,
            CurrentWorldId = 402,
            CharacterHomeWorldId = 403,
            CharacterCurrentWorldId = 404,
            ProxyHomeWorldId = 405,
            MemberProxyFreeCompanyId = 9876543210123456789,
        };
        const ulong recordedFreeCompanyId = 9235334740282912345;

        var output = string.Join("\n", diagnostics.FormatLines(recordedFreeCompanyId));

        foreach (var value in new[] { "401", "402", "403", "404", "405", "9235199887244466359",
                     "9876543210123456789", "9235334740282912345" })
            Assert.Contains(value, output);
        Assert.Contains(diagnostics.Resolve().Reason, output);
    }

    [Fact]
    public void FormattedDiagnosticsDistinguishUnknownReadsFromARecordedConfirmedAbsence()
    {
        var diagnostics = Ready with
        {
            CurrentWorldId = null,
            HasFreeCompanyTag = false,
            ProxyFreeCompanyId = null,
            ProxyHomeWorldId = null,
            HasFreeCompanyName = false,
            MemberProxyFreeCompanyId = null,
        };

        var lines = diagnostics.FormatLines(0);
        var output = string.Join("\n", lines);

        Assert.StartsWith("FC association: unknown.", lines[0]);
        Assert.Contains("Recorded wallet FC: none (0).", output);
        Assert.Contains("tag=empty", output);
        Assert.Contains("name=empty", output);
        Assert.Contains("proxy ID=unavailable", output);
        Assert.Contains("member-proxy ID=unavailable", output);
    }

    [Fact]
    public void FormattedDiagnosticsDistinguishConfirmedAbsenceFromAnUnknownRecordedAssociation()
    {
        var diagnostics = Ready with { HasFreeCompanyTag = false, ProxyFreeCompanyId = 0 };

        var lines = diagnostics.FormatLines(null);

        Assert.StartsWith("FC association: none (0).", lines[0]);
        Assert.Contains("Recorded wallet FC: unknown.", string.Join("\n", lines));
    }
}
