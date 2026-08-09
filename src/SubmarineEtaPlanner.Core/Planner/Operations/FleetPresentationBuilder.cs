namespace SubmarineEtaPlanner.Planner;

public static class FleetPresentationBuilder
{
    public static FcOperationalProjection Create(
        FcState fc,
        EtaResult? result,
        EtaSettings effectiveSettings,
        ISubmarineCatalog catalog,
        DateTimeOffset now,
        IReadOnlyDictionary<long, SubmarineAssignment>? submarineAssignments = null)
    {
        var resultBySubmarine = result?.PerSubResults.ToDictionary(item => item.SubmarineId) ?? [];
        var projections = fc.Submarines
            .Select(submarine => CreateSubmarine(
                submarine,
                resultBySubmarine.GetValueOrDefault(submarine.SubmarineId),
                effectiveSettings,
                catalog,
                now,
                submarineAssignments?.GetValueOrDefault(submarine.SubmarineId) ?? SubmarineAssignment.Auto))
            .OrderBy(submarine => submarine.NeedsImmediateAction ? 0 : 1)
            .ThenBy(submarine => submarine.NextActionAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(submarine => submarine.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var roleSummary = new FcRoleSummary(
            projections.Count(submarine => submarine.EffectiveRole == EffectiveSubmarineRole.Leveling),
            projections.Count(submarine => submarine.EffectiveRole == EffectiveSubmarineRole.Farming),
            projections.Count(submarine => submarine.EffectiveRole == EffectiveSubmarineRole.Paused));
        var farming = fc.Submarines.Count > 0 && fc.Submarines.All(submarine => submarine.Rank >= effectiveSettings.TargetRank);
        var forecast = result?.CompletionForecast;
        return new FcOperationalProjection(
            fc,
            result,
            effectiveSettings.TargetRank,
            farming ? FleetMode.Farming : FleetMode.Leveling,
            projections,
            forecast?.P50AtUtc ?? result?.FcCompletionAtUtc,
            forecast?.P10AtUtc,
            forecast?.P90AtUtc)
        {
            RoleSummary = roleSummary,
        };
    }

    private static SubmarineOperationalProjection CreateSubmarine(
        SubmarineState submarine,
        PerSubEtaResult? result,
        EtaSettings settings,
        ISubmarineCatalog catalog,
        DateTimeOffset now,
        SubmarineAssignment assignment)
    {
        var progress = CurrentVoyageProgressFormatter.Create(submarine, catalog, now);
        var trackedBuild = catalog.ResolveBuild(submarine.BuildParts, submarine.Rank);
        var currentBuild = CurrentBuildPresentation.Create(trackedBuild);
        var state = !submarine.CurrentVoyageKnown && submarine.ReturnAtUtc != DateTimeOffset.MinValue
            ? OperationalState.Syncing
            : progress.State switch
        {
            CurrentVoyageProgressState.Underway => OperationalState.Underway,
            CurrentVoyageProgressState.ReadyToCollect => OperationalState.ReadyToCollect,
            CurrentVoyageProgressState.Syncing => OperationalState.Syncing,
            _ => OperationalState.Idle,
        };
        var ready = submarine.Rank >= settings.TargetRank;
        var route = ready
            ? state is OperationalState.Underway or OperationalState.ReadyToCollect ? submarine.CurrentRoute : []
            : state is OperationalState.Underway or OperationalState.ReadyToCollect
                ? submarine.CurrentRoute
                : result?.NextRoute ?? [];
        var plannedVoyage = result?.VoyagePreview.FirstOrDefault(plan => plan.Route.SequenceEqual(route)) ??
                            result?.VoyagePreview.FirstOrDefault();
        var purpose = ready
            ? RoutePurpose.Farming
            : plannedVoyage?.UnlockObjective is not null ||
              plannedVoyage is { UnlocksApplied.Count: > 0 } ||
              plannedVoyage?.DependsOnProjectedUnlocks == true
                ? RoutePurpose.Unlock
                : route.Count > 0 ? RoutePurpose.Leveling : RoutePurpose.Unknown;

        uint? expectedExp = null;
        int? projectedRank = null;
        string? unavailableReason = null;
        if (route.Count == 0)
        {
            unavailableReason = ready
                ? "No previous or current farming route is available."
                : "A route is not available until the forecast completes.";
        }
        else if (plannedVoyage is not null && !ready && state == OperationalState.Idle)
        {
            expectedExp = plannedVoyage.ExpGain;
            projectedRank = plannedVoyage.RankAfter;
        }
        else
        {
            if (trackedBuild is null)
            {
                unavailableReason = "The recorded submarine build is incomplete.";
            }
            else
            {
                expectedExp = catalog.CalculateExp(route, trackedBuild, settings.GetEffectiveExpMode());
                projectedRank = catalog.ApplyExp(
                    submarine.Rank,
                    submarine.CurrentExp,
                    expectedExp.Value,
                    catalog.MaximumRank).Rank;
            }
        }

        var effectiveRole = SubmarineRoleResolver.Resolve(assignment, submarine.Rank, settings.TargetRank);
        var action = SelectAction(state, effectiveRole, route.Count > 0);
        var stateLabel = state switch
        {
            OperationalState.ReadyToCollect => "Ready to collect",
            OperationalState.Underway => $"Returns {CurrentVoyageProgressFormatter.FormatCountdown(submarine.ReturnAtUtc - now)}",
            OperationalState.Syncing => "Waiting for SubmarineTracker sync",
            _ => "Idle",
        };
        var immediate = state == OperationalState.ReadyToCollect || state == OperationalState.Idle;
        DateTimeOffset? nextAction = immediate
            ? now
            : submarine.ReturnAtUtc == DateTimeOffset.MinValue ? null : submarine.ReturnAtUtc;
        var includedInLevelingTarget = result?.IncludedInLevelingTarget == true;
        return new SubmarineOperationalProjection(
            submarine.SubmarineId,
            submarine.Name,
            submarine.Rank,
            settings.TargetRank,
            state,
            stateLabel,
            action,
            immediate,
            nextAction,
            route,
            ready && route.Count > 0 ? route : result?.NextRoute ?? [],
            purpose,
            expectedExp,
            projectedRank,
            includedInLevelingTarget ? ready ? now : result?.EtaAtUtc : null,
            includedInLevelingTarget ? ready ? 0 : result?.VoyageCount ?? 0 : 0,
            unavailableReason,
            result?.NextRouteOutcomes ?? [])
        {
            CurrentBuild = currentBuild,
            EffectiveRole = effectiveRole,
        };
    }

    private static RecommendedAction SelectAction(
        OperationalState state,
        EffectiveSubmarineRole role,
        bool hasKnownRoute)
    {
        if (role == EffectiveSubmarineRole.Paused)
            return RecommendedAction.Paused;
        if (state == OperationalState.Syncing)
            return RecommendedAction.WaitForTracker;
        if (role == EffectiveSubmarineRole.Farming)
        {
            if (!hasKnownRoute)
                return RecommendedAction.ChooseFarmingRoute;
            return state == OperationalState.ReadyToCollect
                ? RecommendedAction.CollectAndResendFarmingRouteNow
                : state == OperationalState.Idle
                    ? RecommendedAction.SendFarmingRouteNow
                    : RecommendedAction.ResendFarmingRouteAfterCollection;
        }

        return state switch
        {
            OperationalState.ReadyToCollect => RecommendedAction.CollectThenWaitForTracker,
            OperationalState.Idle => RecommendedAction.SendLevelingRouteNow,
            _ => RecommendedAction.SendLevelingRouteAfterCollection,
        };
    }
}
