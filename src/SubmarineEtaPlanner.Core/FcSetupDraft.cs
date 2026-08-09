using System.Collections.ObjectModel;
using System.Globalization;
using SubmarineEtaPlanner.Planner;

namespace SubmarineEtaPlanner;

public sealed record PinnedFarmingRouteParseResult(
    IReadOnlyList<uint> SectorIds,
    IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public string ErrorMessage => string.Join(" ", Errors);
}

public static class PinnedFarmingRouteParser
{
    private static readonly char[] Separators = [',', ' ', '\t', '\r', '\n'];

    public static PinnedFarmingRouteParseResult Parse(
        string? input,
        Func<uint, bool> isKnownSector)
    {
        ArgumentNullException.ThrowIfNull(isKnownSector);

        if (string.IsNullOrWhiteSpace(input))
        {
            return new PinnedFarmingRouteParseResult(
                [],
                ["Enter at least one positive sector ID, or use Clear pin."]);
        }

        var sectorIds = new List<uint>();
        var errors = new List<string>();
        var seen = new HashSet<uint>();
        var tokens = input.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return new PinnedFarmingRouteParseResult(
                [],
                ["Enter at least one positive sector ID, or use Clear pin."]);
        }

        foreach (var token in tokens)
        {
            if (!long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ||
                value <= 0 ||
                value > uint.MaxValue)
            {
                errors.Add($"'{token}' is not a positive sector ID.");
                continue;
            }

            var sectorId = checked((uint)value);
            if (seen.Add(sectorId))
                sectorIds.Add(sectorId);
        }

        var unknownSectorIds = sectorIds.Where(sectorId => !isKnownSector(sectorId)).ToArray();
        if (unknownSectorIds.Length > 0)
            errors.Add($"Unknown sector IDs: {string.Join(", ", unknownSectorIds)}.");

        return new PinnedFarmingRouteParseResult(
            new ReadOnlyCollection<uint>(sectorIds),
            new ReadOnlyCollection<string>(errors));
    }
}

public sealed record SubmarineSetupDraft(
    SubmarineAssignment Assignment,
    IReadOnlyList<uint>? PinnedFarmingRoute)
{
    public static SubmarineSetupDraft Automatic { get; } = new(SubmarineAssignment.Auto, null);

    public SubmarineSetupDraft WithPinnedFarmingRoute(IReadOnlyList<uint>? route)
        => this with { PinnedFarmingRoute = CopyRoute(route) };

    internal static IReadOnlyList<uint>? CopyRoute(IReadOnlyList<uint>? route)
        => route is null || route.Count == 0
            ? null
            : new ReadOnlyCollection<uint>(route.ToArray());
}

public sealed record FcSetupDraft(
    int? TargetRankOverride,
    FcStrategyPreset? StrategyOverride,
    IReadOnlyDictionary<long, SubmarineSetupDraft> Submarines)
{
    public static FcSetupDraft Capture(
        FcPreferences preferences,
        IEnumerable<long> submarineIds)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(submarineIds);

        var submarines = submarineIds
            .Distinct()
            .ToDictionary(
                submarineId => submarineId,
                submarineId => preferences.Submarines is not null &&
                               preferences.Submarines.TryGetValue(submarineId, out var saved) &&
                               saved is not null
                    ? new SubmarineSetupDraft(
                        saved.Assignment,
                        SubmarineSetupDraft.CopyRoute(saved.PinnedFarmingRoute))
                    : SubmarineSetupDraft.Automatic);
        return new FcSetupDraft(
            preferences.TargetRankOverride,
            preferences.StrategyOverride,
            new ReadOnlyDictionary<long, SubmarineSetupDraft>(submarines));
    }

    public FcSetupDraft WithSubmarine(long submarineId, SubmarineSetupDraft submarine)
    {
        ArgumentNullException.ThrowIfNull(submarine);
        var submarines = Submarines.ToDictionary(pair => pair.Key, pair => pair.Value);
        submarines[submarineId] = submarine with
        {
            PinnedFarmingRoute = SubmarineSetupDraft.CopyRoute(submarine.PinnedFarmingRoute),
        };
        return this with
        {
            Submarines = new ReadOnlyDictionary<long, SubmarineSetupDraft>(submarines),
        };
    }

    public FcSetupDraftApplyResult ApplyTo(FcPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.Submarines ??= [];

        var fcSettingsChanged = preferences.TargetRankOverride != TargetRankOverride ||
                                preferences.StrategyOverride != StrategyOverride;
        preferences.TargetRankOverride = TargetRankOverride;
        preferences.StrategyOverride = StrategyOverride;

        var assignmentChanged = false;
        var pinnedRouteChanged = false;
        foreach (var (submarineId, draft) in Submarines)
        {
            preferences.Submarines.TryGetValue(submarineId, out var saved);
            var savedAssignment = saved?.Assignment ?? SubmarineAssignment.Auto;
            var savedRoute = SubmarineSetupDraft.CopyRoute(saved?.PinnedFarmingRoute);
            var draftRoute = SubmarineSetupDraft.CopyRoute(draft.PinnedFarmingRoute);

            assignmentChanged |= savedAssignment != draft.Assignment;
            pinnedRouteChanged |= !RoutesEqual(savedRoute, draftRoute);

            if (draft.Assignment == SubmarineAssignment.Auto && draftRoute is null)
            {
                if (saved?.CollectionDelayMinutes is null)
                    preferences.Submarines.Remove(submarineId);
                else
                {
                    saved.Assignment = SubmarineAssignment.Auto;
                    saved.PinnedFarmingRoute = null;
                }
                continue;
            }

            saved ??= new SubmarinePreferences();
            saved.Assignment = draft.Assignment;
            saved.PinnedFarmingRoute = draftRoute?.ToList();
            preferences.Submarines[submarineId] = saved;
        }

        return new FcSetupDraftApplyResult(
            fcSettingsChanged,
            assignmentChanged,
            pinnedRouteChanged);
    }

    private static bool RoutesEqual(IReadOnlyList<uint>? left, IReadOnlyList<uint>? right)
        => left is null ? right is null : right is not null && left.SequenceEqual(right);
}

public sealed record FcSetupDraftApplyResult(
    bool FcSettingsChanged,
    bool AssignmentChanged,
    bool PinnedRouteChanged)
{
    public bool EtaRefreshRequired => FcSettingsChanged || AssignmentChanged;
}
