namespace SubmarineEtaPlanner.Balances;

/// <summary>A copy of the scalar inputs used to associate the current wallet with an FC.</summary>
internal sealed record BalanceAssociationDiagnostics(
    uint HomeWorldId,
    uint? CurrentWorldId,
    uint CharacterHomeWorldId,
    uint CharacterCurrentWorldId,
    bool HasFreeCompanyTag,
    ulong? ProxyFreeCompanyId,
    uint? ProxyHomeWorldId,
    bool HasFreeCompanyName,
    ulong? MemberProxyFreeCompanyId)
{
    public (ulong? FreeCompanyId, string Reason) Resolve()
    {
        if (CurrentWorldId is null)
            return (null, "The player-state current world is unavailable.");
        if (CurrentWorldId != HomeWorldId)
            return (null, "The player-state current world differs from the home world.");
        if (CharacterCurrentWorldId != HomeWorldId || CharacterHomeWorldId != HomeWorldId)
            return (null, "The character world IDs disagree with the player-state home world.");
        if (ProxyFreeCompanyId is null)
            return (null, "The FC proxy is unavailable.");
        if (!HasFreeCompanyTag)
            return ProxyFreeCompanyId == 0
                ? (0UL, "No FC reported: the character tag is empty and the proxy FC ID is zero.")
                : (null, "The character FC tag is empty although the proxy FC ID is nonzero.");
        if (ProxyFreeCompanyId == 0)
            return (null, "The character has an FC tag but the proxy FC ID is zero.");
        if (!HasFreeCompanyName)
            return (null, "The proxy FC ID is nonzero but the FC name is empty.");
        return (ProxyFreeCompanyId, "The current reader checks accept the FC association.");
    }

    public IReadOnlyList<string> FormatLines(ulong? recordedFreeCompanyId)
    {
        var (freeCompanyId, reason) = Resolve();
        return
        [
            $"FC association: {FormatAssociation(freeCompanyId)}. {reason}",
            $"World IDs: player-state home={HomeWorldId}, current={FormatValue(CurrentWorldId)}; "
                + $"character home={CharacterHomeWorldId}, current={CharacterCurrentWorldId}.",
            $"FC data: tag={(HasFreeCompanyTag ? "present" : "empty")}; "
                + $"proxy ID={FormatValue(ProxyFreeCompanyId)}, home={FormatValue(ProxyHomeWorldId)}, "
                + $"name={(HasFreeCompanyName ? "present" : "empty")}; member-proxy ID={FormatValue(MemberProxyFreeCompanyId)}.",
            $"Recorded wallet FC: {FormatAssociation(recordedFreeCompanyId)}.",
        ];
    }

    private static string FormatAssociation(ulong? value)
        => value is null ? "unknown" : value == 0 ? "none (0)" : value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatValue<T>(T? value) where T : struct, IFormattable
        => value?.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ?? "unavailable";
}
