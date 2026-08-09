using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using SubmarineEtaPlanner.Planner;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

public sealed partial class PlannerWindow
{
    private string? selectedSetupFcId;
    private string? pendingSetupFcId;
    private bool setupDraftDirty;
    private bool setupUseGlobalTarget = true;
    private int setupTargetRank;
    private FcStrategyPreset? setupStrategy;
    private readonly Dictionary<long, SubmarineSetupDraft> setupSubmarineDrafts = [];
    private readonly HashSet<long> setupRouteEditors = [];
    private readonly Dictionary<long, string> setupRouteInputs = [];
    private readonly Dictionary<long, PinnedFarmingRouteParseResult> setupRouteValidation = [];

    private void DrawFcSetupPage()
    {
        var currentSnapshot = EnsureFleetSnapshot();
        if (currentSnapshot is null || currentSnapshot.FreeCompanies.Count == 0)
            return;

        var ordered = currentSnapshot.FreeCompanies
            .OrderByDescending(fc => this.configuration.GetFcPreferences(fc.FcIdKey).Favorite)
            .ThenBy(fc => fc.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (this.selectedSetupFcId is null || ordered.All(fc => fc.FcIdKey != this.selectedSetupFcId))
            SelectSetupFc(ordered[0].FcIdKey);
        var selected = ordered.First(fc => fc.FcIdKey == this.selectedSetupFcId);

        ImGui.SetNextItemWidth(Math.Min(420f * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo("Free company##setup-fc", selected.DisplayName))
        {
            foreach (var fc in ordered)
            {
                var favoritePrefix = this.configuration.GetFcPreferences(fc.FcIdKey).Favorite ? "★ " : string.Empty;
                if (ImGui.Selectable($"{favoritePrefix}{fc.DisplayName}##select-{fc.FcIdKey}", fc.FcIdKey == selected.FcIdKey))
                    RequestSetupFcSelection(fc.FcIdKey);
            }
            ImGui.EndCombo();
        }

        ImGui.Spacing();
        BeginSettingsCard("fc-preference-card", selected.DisplayName, "Favorites save immediately. Target, strategy, assignment, and pinned-route changes remain staged until Save.");
        var preferences = this.configuration.GetFcPreferences(selected.FcIdKey);
        var favorite = preferences.Favorite;
        SettingLabel("Favorite", "Favorite FCs remain above non-favorites on Operations, Leveling, and Income.");
        if (ImGui.Checkbox("Pin this free company##favorite-fc", ref favorite))
        {
            preferences.Favorite = favorite;
            this.saveConfiguration();
        }

        SettingLabel("Target rank", $"Use the global target ({this.configuration.Settings.TargetRank}) or override it for this FC.");
        var useGlobalTarget = this.setupUseGlobalTarget;
        if (ImGui.Checkbox("Use global target##setup-global-target", ref useGlobalTarget))
        {
            this.setupUseGlobalTarget = useGlobalTarget;
            this.setupDraftDirty = true;
        }
        if (!this.setupUseGlobalTarget)
        {
            ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
            var target = this.setupTargetRank;
            if (ImGui.InputInt("Rank##setup-target-rank", ref target))
            {
                this.setupTargetRank = Math.Clamp(target, 1, this.catalog.MaximumRank);
                this.setupDraftDirty = true;
            }
        }

        SettingLabel("Leveling strategy", "Recommended unlocks missing slots and required main leveling routes, then selects the best expected EXP/hour.");
        if (DrawStrategyCombo(ref this.setupStrategy))
            this.setupDraftDirty = true;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        SettingLabel(
            "Submarine assignments",
            "Tracked routes come from SubmarineTracker. A pinned farming route is a separate saved route used by farming projections.");
        DrawSubmarineSetupTable(selected);
        EndSettingsCard();

        var routesValid = SetupRoutesAreValid();
        if (!routesValid)
            PlannerUi.DrawStatusPill("Fix invalid pinned routes", PlannerUi.Amber);
        else if (this.setupDraftDirty)
            PlannerUi.DrawStatusPill("Unsaved FC changes", PlannerUi.Amber);
        else
            PlannerUi.DrawStatusPill("FC settings up to date", PlannerUi.Green);
        ImGui.SameLine();
        if (!routesValid)
            ImGui.BeginDisabled();
        if (PlannerUi.IconButtonWithText("save-fc-settings", FontAwesomeIcon.Check, "Save"))
            SaveSetupDraft();
        if (!routesValid)
            ImGui.EndDisabled();
        ImGui.SameLine();
        if (PlannerUi.IconButtonWithText("reset-fc-settings", FontAwesomeIcon.Undo, "Reset to global"))
        {
            this.setupUseGlobalTarget = true;
            this.setupStrategy = null;
            this.setupDraftDirty = true;
        }
        ImGui.SameLine();
        if (PlannerUi.IconButtonWithText("revert-fc-settings", FontAwesomeIcon.Times, "Revert"))
            SelectSetupFc(selected.FcIdKey);

        DrawUnsavedSetupModal();
    }

    private bool DrawStrategyCombo(ref FcStrategyPreset? strategy)
    {
        var current = strategy is null ? 0 : (int)strategy.Value + 1;
        string[] labels =
        [
            $"Inherit global ({EtaModelLabels[(int)this.configuration.Settings.EtaModel]})",
            "Recommended",
            "Advanced · Immediate EXP only",
            "Advanced · Slots first, then immediate EXP",
            "Advanced · Unlock everything, then level",
        ];
        var changed = false;
        ImGui.SetNextItemWidth(Math.Min(470f * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo("##setup-strategy", labels[current]))
        {
            for (var index = 0; index < labels.Length; index++)
            {
                if (ImGui.Selectable(labels[index], index == current))
                {
                    strategy = index == 0 ? null : (FcStrategyPreset)(index - 1);
                    changed = true;
                }
                if (ImGui.IsItemHovered())
                    PlannerUi.Tooltip(index switch
                    {
                        1 => "Unlock missing submarine slots and required main leveling routes, then use best expected EXP/hour.",
                        2 => "Use the best currently available EXP route without deliberately chasing unlock objectives.",
                        3 => "Unlock missing submarine slots first, then use the best currently available EXP route.",
                        4 => "Deliberately unlock every reachable destination before pure leveling.",
                        _ => "Use the global simulation and route settings.",
                    });
            }
            ImGui.EndCombo();
        }
        return changed;
    }

    private void DrawSubmarineSetupTable(FcState selected)
    {
        if (selected.Submarines.Count == 0)
        {
            ImGui.TextColored(PlannerUi.Muted, "No submarines are recorded for this free company.");
            return;
        }

        const float minimumWidth = 940f;
        var scaledMinimumWidth = minimumWidth * ImGuiHelpers.GlobalScale;
        var needsHorizontalScroll = ImGui.GetContentRegionAvail().X < scaledMinimumWidth;
        var flags = ImGuiTableFlags.Borders |
                    ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.Resizable |
                    ImGuiTableFlags.SizingStretchProp;
        if (needsHorizontalScroll)
            flags |= ImGuiTableFlags.ScrollX;

        if (!ImGui.BeginTable(
                $"setup-submarines-{selected.FcIdKey}",
                5,
                flags,
                Vector2.Zero,
                needsHorizontalScroll ? scaledMinimumWidth : 0f))
            return;

        ImGui.TableSetupColumn("Submarine", ImGuiTableColumnFlags.WidthFixed, 145f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Rank", ImGuiTableColumnFlags.WidthFixed, 58f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Tracked route", ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn("Assignment", ImGuiTableColumnFlags.WidthFixed, 180f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Pinned farming route", ImGuiTableColumnFlags.WidthStretch, 1.35f);
        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableHeadersRow();

        var effectiveTargetRank = this.setupUseGlobalTarget
            ? this.configuration.Settings.TargetRank
            : Math.Clamp(this.setupTargetRank, 1, this.catalog.MaximumRank);
        foreach (var submarine in selected.Submarines.OrderBy(submarine => submarine.SubmarineId))
        {
            var draft = this.setupSubmarineDrafts.GetValueOrDefault(
                submarine.SubmarineId,
                SubmarineSetupDraft.Automatic);

            ImGui.TableNextRow();
            DrawTableText(submarine.Name);
            DrawTableText(submarine.Rank.ToString());

            ImGui.TableNextColumn();
            if (!submarine.CurrentVoyageKnown)
            {
                ImGui.TextColored(PlannerUi.Amber, "Unknown");
                PlannerUi.Tooltip("SubmarineTracker could not identify the currently tracked voyage route.");
            }
            else if (submarine.CurrentRoute.Count == 0)
            {
                ImGui.TextColored(PlannerUi.Muted, "No tracked route");
            }
            else
            {
                ImGui.TextColored(PlannerUi.Cyan, FormatRoute(submarine.CurrentRoute));
                PlannerUi.Tooltip("Tracked route from SubmarineTracker.");
            }

            ImGui.TableNextColumn();
            DrawAssignmentCombo(submarine, draft, effectiveTargetRank);

            ImGui.TableNextColumn();
            DrawPinnedRouteControls(submarine, draft);
        }

        ImGui.EndTable();
    }

    private void DrawAssignmentCombo(
        SubmarineState submarine,
        SubmarineSetupDraft draft,
        int effectiveTargetRank)
    {
        var effectiveRole = SubmarineRoleResolver.Resolve(
            draft.Assignment,
            submarine.Rank,
            effectiveTargetRank);
        var preview = AssignmentLabel(draft.Assignment, effectiveRole);
        ImGui.SetNextItemWidth(-1);
        if (!ImGui.BeginCombo($"##setup-assignment-{submarine.SubmarineId}", preview))
            return;

        foreach (var assignment in Enum.GetValues<SubmarineAssignment>())
        {
            var optionRole = SubmarineRoleResolver.Resolve(assignment, submarine.Rank, effectiveTargetRank);
            var label = AssignmentLabel(assignment, optionRole);
            if (ImGui.Selectable(
                    $"{label}##setup-assignment-{submarine.SubmarineId}-{assignment}",
                    draft.Assignment == assignment))
            {
                this.setupSubmarineDrafts[submarine.SubmarineId] = draft with { Assignment = assignment };
                this.setupDraftDirty = true;
            }
        }
        ImGui.EndCombo();
    }

    private void DrawPinnedRouteControls(SubmarineState submarine, SubmarineSetupDraft draft)
    {
        if (draft.PinnedFarmingRoute is { Count: > 0 } pinnedRoute)
        {
            ImGui.TextColored(PlannerUi.Teal, $"Pinned: {FormatRoute(pinnedRoute)}");
            PlannerUi.Tooltip("Saved farming route. This is independent of the tracked voyage route.");
        }
        else
        {
            ImGui.TextColored(PlannerUi.Muted, "No pin — tracked route is used");
        }

        var canUseTrackedRoute = submarine.CurrentVoyageKnown && submarine.CurrentRoute.Count > 0;
        if (!canUseTrackedRoute)
            ImGui.BeginDisabled();
        if (ImGui.SmallButton($"Use tracked route##setup-use-tracked-{submarine.SubmarineId}"))
            SetPinnedRouteFromTracked(submarine);
        if (!canUseTrackedRoute)
            ImGui.EndDisabled();
        if (!canUseTrackedRoute)
            PlannerUi.Tooltip("No known tracked route is available to pin.");

        ImGui.SameLine();
        if (ImGui.SmallButton($"Edit##setup-edit-route-{submarine.SubmarineId}"))
        {
            this.setupRouteEditors.Add(submarine.SubmarineId);
            var input = draft.PinnedFarmingRoute is { Count: > 0 }
                ? string.Join(", ", draft.PinnedFarmingRoute)
                : string.Empty;
            this.setupRouteInputs[submarine.SubmarineId] = input;
            this.setupRouteValidation[submarine.SubmarineId] = ParsePinnedRoute(input);
        }

        ImGui.SameLine();
        var hasPin = draft.PinnedFarmingRoute is { Count: > 0 };
        if (!hasPin)
            ImGui.BeginDisabled();
        if (ImGui.SmallButton($"Clear pin##setup-clear-route-{submarine.SubmarineId}"))
        {
            this.setupSubmarineDrafts[submarine.SubmarineId] = draft.WithPinnedFarmingRoute(null);
            ClosePinnedRouteEditor(submarine.SubmarineId);
            this.setupDraftDirty = true;
        }
        if (!hasPin)
            ImGui.EndDisabled();

        if (!this.setupRouteEditors.Contains(submarine.SubmarineId))
            return;

        var routeInput = this.setupRouteInputs.GetValueOrDefault(submarine.SubmarineId, string.Empty);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint(
                $"##setup-route-input-{submarine.SubmarineId}",
                "Sector IDs, e.g. 7, 12, 18",
                ref routeInput,
                256))
        {
            this.setupRouteInputs[submarine.SubmarineId] = routeInput;
            var validation = ParsePinnedRoute(routeInput);
            this.setupRouteValidation[submarine.SubmarineId] = validation;
            if (validation.IsValid)
            {
                this.setupSubmarineDrafts[submarine.SubmarineId] = draft.WithPinnedFarmingRoute(validation.SectorIds);
                this.setupDraftDirty = true;
            }
        }

        var result = this.setupRouteValidation.GetValueOrDefault(submarine.SubmarineId) ?? ParsePinnedRoute(routeInput);
        this.setupRouteValidation[submarine.SubmarineId] = result;
        if (!result.IsValid)
        {
            ImGui.TextColored(PlannerUi.Amber, result.ErrorMessage);
            return;
        }

        ImGui.TextColored(
            PlannerUi.Muted,
            $"Preview: {string.Join(" → ", result.SectorIds.Select(this.catalog.PointName))}");
    }

    private void SetPinnedRouteFromTracked(SubmarineState submarine)
    {
        var input = string.Join(", ", submarine.CurrentRoute);
        var validation = ParsePinnedRoute(input);
        if (!validation.IsValid)
        {
            this.setupRouteEditors.Add(submarine.SubmarineId);
            this.setupRouteInputs[submarine.SubmarineId] = input;
            this.setupRouteValidation[submarine.SubmarineId] = validation;
            return;
        }

        var draft = this.setupSubmarineDrafts.GetValueOrDefault(
            submarine.SubmarineId,
            SubmarineSetupDraft.Automatic);
        this.setupSubmarineDrafts[submarine.SubmarineId] = draft.WithPinnedFarmingRoute(validation.SectorIds);
        ClosePinnedRouteEditor(submarine.SubmarineId);
        this.setupDraftDirty = true;
    }

    private PinnedFarmingRouteParseResult ParsePinnedRoute(string input)
        => PinnedFarmingRouteParser.Parse(
            input,
            sectorId => this.catalog.GetPointRequiredRank(sectorId) != int.MaxValue);

    private bool SetupRoutesAreValid()
        => this.setupRouteEditors.All(submarineId =>
            this.setupRouteValidation.TryGetValue(submarineId, out var result) && result.IsValid);

    private void ClosePinnedRouteEditor(long submarineId)
    {
        this.setupRouteEditors.Remove(submarineId);
        this.setupRouteInputs.Remove(submarineId);
        this.setupRouteValidation.Remove(submarineId);
    }

    private static string AssignmentLabel(
        SubmarineAssignment assignment,
        EffectiveSubmarineRole effectiveRole)
        => assignment == SubmarineAssignment.Auto
            ? $"Automatic — {effectiveRole}"
            : assignment.ToString();

    private void RequestSetupFcSelection(string fcIdKey)
    {
        if (fcIdKey == this.selectedSetupFcId)
            return;
        if (!this.setupDraftDirty)
        {
            SelectSetupFc(fcIdKey);
            return;
        }
        this.pendingSetupFcId = fcIdKey;
        ImGui.OpenPopup("Unsaved FC changes###unsaved-fc-setup");
    }

    private void SelectSetupFc(string fcIdKey)
    {
        this.selectedSetupFcId = fcIdKey;
        var preferences = this.configuration.GetFcPreferences(fcIdKey);
        this.setupUseGlobalTarget = preferences.TargetRankOverride is null;
        this.setupTargetRank = preferences.TargetRankOverride ?? this.configuration.Settings.TargetRank;
        this.setupStrategy = preferences.StrategyOverride;
        this.setupSubmarineDrafts.Clear();
        var fleet = this.snapshot?.FreeCompanies.FirstOrDefault(fc => fc.FcIdKey == fcIdKey);
        var draft = FcSetupDraft.Capture(
            preferences,
            fleet?.Submarines.Select(submarine => submarine.SubmarineId) ?? []);
        foreach (var (submarineId, submarineDraft) in draft.Submarines)
            this.setupSubmarineDrafts[submarineId] = submarineDraft;
        this.setupRouteEditors.Clear();
        this.setupRouteInputs.Clear();
        this.setupRouteValidation.Clear();
        this.setupDraftDirty = false;
    }

    private void SaveSetupDraft()
    {
        if (this.selectedSetupFcId is null)
            return;
        if (!SetupRoutesAreValid())
            return;
        var preferences = this.configuration.GetFcPreferences(this.selectedSetupFcId);
        var draft = FcSetupDraft.Capture(preferences, this.setupSubmarineDrafts.Keys) with
        {
            TargetRankOverride = this.setupUseGlobalTarget
                ? null
                : Math.Clamp(this.setupTargetRank, 1, this.catalog.MaximumRank),
            StrategyOverride = this.setupStrategy,
        };
        foreach (var (submarineId, submarineDraft) in this.setupSubmarineDrafts)
            draft = draft.WithSubmarine(submarineId, submarineDraft);
        var applyResult = draft.ApplyTo(preferences);
        this.setupDraftDirty = false;
        this.setupRouteEditors.Clear();
        this.setupRouteInputs.Clear();
        this.setupRouteValidation.Clear();
        this.saveConfiguration();
        if (applyResult.EtaRefreshRequired)
            QueueRefresh(ForecastRefreshMode.Incremental);
    }

    private void DrawUnsavedSetupModal()
    {
        if (!ImGui.BeginPopupModal("Unsaved FC changes###unsaved-fc-setup", ImGuiWindowFlags.AlwaysAutoResize))
            return;
        ImGui.TextWrapped("Save the staged FC setup changes before opening another free company?");
        ImGui.Spacing();
        var routesValid = SetupRoutesAreValid();
        if (!routesValid)
        {
            ImGui.TextColored(PlannerUi.Amber, "Fix invalid pinned routes before saving.");
            ImGui.Spacing();
            ImGui.BeginDisabled();
        }
        if (PlannerUi.IconButtonWithText("save-switch-fc", FontAwesomeIcon.Check, "Save"))
        {
            SaveSetupDraft();
            if (this.pendingSetupFcId is { } pending)
                SelectSetupFc(pending);
            this.pendingSetupFcId = null;
            ImGui.CloseCurrentPopup();
        }
        if (!routesValid)
            ImGui.EndDisabled();
        ImGui.SameLine();
        if (PlannerUi.IconButtonWithText("discard-switch-fc", FontAwesomeIcon.Trash, "Discard"))
        {
            if (this.pendingSetupFcId is { } pending)
                SelectSetupFc(pending);
            this.pendingSetupFcId = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (PlannerUi.IconButtonWithText("stay-on-fc", FontAwesomeIcon.Times, "Stay"))
        {
            this.pendingSetupFcId = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }
}
