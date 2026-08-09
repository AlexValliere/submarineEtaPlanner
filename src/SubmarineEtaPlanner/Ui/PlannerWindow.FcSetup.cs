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
        BeginSettingsCard("fc-preference-card", selected.DisplayName, "Favorites save immediately. Target and strategy changes remain staged until Save.");
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
        EndSettingsCard();

        if (this.setupDraftDirty)
            PlannerUi.DrawStatusPill("Unsaved FC changes", PlannerUi.Amber);
        else
            PlannerUi.DrawStatusPill("FC settings up to date", PlannerUi.Green);
        ImGui.SameLine();
        if (PlannerUi.IconButtonWithText("save-fc-settings", FontAwesomeIcon.Check, "Save"))
            SaveSetupDraft();
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
        this.setupDraftDirty = false;
    }

    private void SaveSetupDraft()
    {
        if (this.selectedSetupFcId is null)
            return;
        var preferences = this.configuration.GetFcPreferences(this.selectedSetupFcId);
        preferences.TargetRankOverride = this.setupUseGlobalTarget ? null : Math.Clamp(this.setupTargetRank, 1, this.catalog.MaximumRank);
        preferences.StrategyOverride = this.setupStrategy;
        this.setupDraftDirty = false;
        this.saveConfiguration();
        QueueRefresh(ForecastRefreshMode.Incremental);
    }

    private void DrawUnsavedSetupModal()
    {
        if (!ImGui.BeginPopupModal("Unsaved FC changes###unsaved-fc-setup", ImGuiWindowFlags.AlwaysAutoResize))
            return;
        ImGui.TextWrapped("Save the target and strategy changes before opening another free company?");
        ImGui.Spacing();
        if (PlannerUi.IconButtonWithText("save-switch-fc", FontAwesomeIcon.Check, "Save"))
        {
            SaveSetupDraft();
            if (this.pendingSetupFcId is { } pending)
                SelectSetupFc(pending);
            this.pendingSetupFcId = null;
            ImGui.CloseCurrentPopup();
        }
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
