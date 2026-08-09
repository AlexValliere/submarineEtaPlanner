namespace SubmarineEtaPlanner.Planner;

internal sealed record OperationsFcHeaderPresentation(
    string FreeCompany,
    string World,
    string Mode,
    string Attention,
    string FarmReady,
    string Ranks,
    bool HasImmediateActions,
    bool IsFarming)
{
    public static OperationsFcHeaderPresentation Create(
        FcOperationalProjection projection,
        bool favorite,
        DateTimeOffset now)
    {
        var attention = projection.ImmediateActionCount > 0
            ? $"{projection.ImmediateActionCount} action{(projection.ImmediateActionCount == 1 ? string.Empty : "s")} now"
            : projection.EarliestFutureReturnAtUtc is { } next
                ? $"In {CurrentVoyageProgressFormatter.FormatCountdown(next - now)}"
                : "No known return";
        var farmReady = projection.Mode == FleetMode.Farming
            ? "Ready"
            : projection.CompletionP50AtUtc is { } eta
                ? FormatFarmReady(eta - now)
                : "Unavailable";
        return new OperationsFcHeaderPresentation(
            $"{(favorite ? "★ " : string.Empty)}{projection.State.FreeCompanyTag}",
            string.IsNullOrWhiteSpace(projection.State.World) ? "—" : projection.State.World,
            projection.Mode.ToString(),
            attention,
            farmReady,
            projection.Submarines.Count == 0
                ? "—"
                : string.Join(" · ", projection.Submarines.Select(submarine => $"R{submarine.Rank}")),
            projection.ImmediateActionCount > 0,
            projection.Mode == FleetMode.Farming);
    }

    private static string FormatFarmReady(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
            return "Now";
        var rounded = TimeSpan.FromMinutes(Math.Ceiling(remaining.TotalMinutes));
        return rounded.Days > 0
            ? $"{rounded.Days}d {rounded.Hours}h"
            : rounded.Hours > 0
                ? $"{rounded.Hours}h {rounded.Minutes}m"
                : $"{rounded.Minutes}m";
    }
}

internal sealed record CompactOperationalStatePresentation(string Label, string Tooltip)
{
    public static CompactOperationalStatePresentation Create(SubmarineOperationalProjection submarine)
    {
        var label = submarine.State switch
        {
            OperationalState.Syncing => "Syncing",
            OperationalState.ReadyToCollect => "To collect",
            OperationalState.Underway => "Underway",
            _ => "Idle",
        };
        return new CompactOperationalStatePresentation(label, $"{submarine.StateLabel}\n{submarine.ActionLabel}");
    }
}

internal sealed record OperationsRankPresentation(string Label, string? Tooltip)
{
    public static OperationsRankPresentation Create(SubmarineOperationalProjection submarine)
        => submarine.ProjectedRank switch
        {
            null => new($"R{submarine.Rank} → ?", submarine.ProjectionUnavailableReason ?? "Projected rank is unavailable."),
            var rank when rank == submarine.Rank => new($"R{submarine.Rank}", null),
            var rank => new($"R{submarine.Rank} → R{rank}", null),
        };
}

internal sealed record OperationsCompletionPresentation(string Label, string Tooltip)
{
    public static OperationsCompletionPresentation Create(FcOperationalProjection projection)
    {
        if (projection.Mode == FleetMode.Farming)
        {
            return new OperationsCompletionPresentation(
                $"Fleet ready · all {projection.Submarines.Count} submarines are at or above R{projection.EffectiveTargetRank}",
                "Every currently tracked submarine has reached this FC's effective target rank.");
        }

        var prefix = $"Target R{projection.EffectiveTargetRank} · {projection.ReadyCount}/{projection.Submarines.Count} ready";
        if (projection.CompletionP50AtUtc is not { } expected)
            return new OperationsCompletionPresentation($"{prefix} · Expected readiness unavailable", "The forecast did not produce a reliable completion date.");
        var label = $"{prefix} · Expected ready around {expected.LocalDateTime:g}";
        if (projection.CompletionP10AtUtc is { } earliest && projection.CompletionP90AtUtc is { } latest)
            label += $" · Likely between {earliest.LocalDateTime:g} and {latest.LocalDateTime:g}";
        return new OperationsCompletionPresentation(
            label,
            "Based on simulated voyage and unlock outcomes; most simulated results completed within the displayed range.");
    }
}

internal sealed record VoyageRoutePurposePresentation(string Label, string Tooltip)
{
    public static VoyageRoutePurposePresentation Create(VoyagePlan plan, Func<uint, string> pointName)
    {
        if (plan.UnlockObjective is not { } objective)
        {
            return new VoyageRoutePurposePresentation(
                "Best available EXP/hour",
                "No intentional unlock objective was selected; this was the best available leveling route.");
        }

        var required = pointName(objective.RequiredPoint);
        var target = pointName(objective.TargetPoint);
        return objective.Kind switch
        {
            UnlockObjectiveKind.ExploreSubmarineSlot => new VoyageRoutePurposePresentation(
                "Unlock submarine slot",
                $"Explore {target} to unlock the next submarine slot."),
            UnlockObjectiveKind.MainProgression => new VoyageRoutePurposePresentation(
                "Continue map progression",
                $"Visit {required} to unlock the next progression destination, {target}."),
            _ => new VoyageRoutePurposePresentation(
                $"Unlock {target}",
                $"Visit {required} to unlock the next destination, {target}."),
        };
    }
}
