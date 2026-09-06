using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using SubmarineEtaPlanner.Planner;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

public sealed partial class PlannerWindow
{
    private void DrawSettingsPage()
    {
        DrawSettingsTabs();
        ImGui.Spacing();
        switch (this.settingsSection)
        {
            case SettingsSection.Simulation:
                DrawSimulationSettings();
                break;
            case SettingsSection.Routes:
                DrawRouteSettings();
                break;
            case SettingsSection.Limits:
                DrawLimitSettings();
                break;
            case SettingsSection.DataSource:
                DrawDataSourceSettings();
                break;
            case SettingsSection.BuildProfile:
                DrawBuildProfileSettings();
                break;
            case SettingsSection.Display:
                DrawDisplaySettings();
                break;
        }
    }

    private void DrawSettingsTabs()
    {
        DrawSettingsTab("Simulation", SettingsSection.Simulation);
        PlannerUi.SameLineIfFits("Routes");
        DrawSettingsTab("Routes", SettingsSection.Routes);
        PlannerUi.SameLineIfFits("Limits");
        DrawSettingsTab("Limits", SettingsSection.Limits);
        PlannerUi.SameLineIfFits("Data source");
        DrawSettingsTab("Data source", SettingsSection.DataSource);
        PlannerUi.SameLineIfFits("Build profile");
        DrawSettingsTab("Build profile", SettingsSection.BuildProfile);
        PlannerUi.SameLineIfFits("Display");
        DrawSettingsTab("Display", SettingsSection.Display);
    }

    private void DrawSettingsTab(string label, SettingsSection section)
    {
        if (PlannerUi.SegmentedButton($"settings-tab-{section}", label, this.settingsSection == section))
            this.settingsSection = section;
    }

    private void DrawSimulationSettings()
    {
        var settings = this.draftSettings;
        var changed = false;
        BeginSettingsCard("simulation-card", "Forecast model", "These settings define the target and how submarine fleets advance toward it.");

        var etaModel = settings.EtaModel;
        SettingLabel("ETA model", "Recommended leveling applies an opinionated leveling preset; Custom strategy uses the advanced controls below.");
        if (DrawEnumCombo("##eta-model", EtaModelLabels, ref etaModel))
        {
            settings.EtaModel = etaModel;
            changed = true;
        }

        var target = settings.TargetRank;
        var maximumRank = Math.Max(1, this.catalog.MaximumRank);
        SettingLabel("Target rank", $"Choose any supported submarine rank from 1 to {maximumRank}.");
        ImGui.SetNextItemWidth(150f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("##target-rank", ref target))
        {
            settings.TargetRank = Math.Clamp(target, 1, maximumRank);
            changed = true;
        }

        var fleetMode = settings.SimulationMode == SimulationMode.Fleet;
        SettingLabel("Fleet simulation", "Coordinate shared unlocks and voyage timing across submarines in the same free company.");
        if (ImGui.Checkbox("Simulate submarines as one fleet##fleet-mode", ref fleetMode))
        {
            settings.SimulationMode = fleetMode ? SimulationMode.Fleet : SimulationMode.OptimisticPerSub;
            changed = true;
        }

        if (settings.EtaModel == EtaModel.PracticalLeveling)
        {
            PlannerUi.Callout(
                "practical-summary",
                FontAwesomeIcon.InfoCircle,
                "Recommended leveling preset",
                "Average EXP  •  expected EXP/hour scoring  •  main leveling-route unlocks  •  standard build progression",
                PlannerUi.Teal);
        }
        else
        {
            var averageExp = settings.ExpMode == ExpMode.Average;
            SettingLabel("EXP estimate", "Average is realistic; guaranteed uses the minimum expected reward.");
            if (ImGui.Checkbox("Use average EXP##average-exp", ref averageExp))
            {
                settings.ExpMode = averageExp ? ExpMode.Average : ExpMode.Guaranteed;
                changed = true;
            }

            var optimize = settings.OptimizeExpPerHour;
            SettingLabel("Route scoring", "Prefer EXP per hour instead of maximum EXP per voyage.");
            if (ImGui.Checkbox("Optimize EXP/hour##optimize-exp", ref optimize))
            {
                settings.OptimizeExpPerHour = optimize;
                changed = true;
            }

            var routeGoal = settings.RouteGoal;
            SettingLabel("Route goal", "Choose which unlock objective the custom strategy should pursue.");
            if (DrawEnumCombo("##route-goal", RouteGoalLabels, ref routeGoal))
            {
                settings.RouteGoal = routeGoal;
                changed = true;
            }
        }

        var delay = settings.CollectionDelayMinutes;
        SettingLabel("Collection delay", "Add a realistic delay between a submarine returning and its next deployment.");
        ImGui.SetNextItemWidth(150f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("Minutes##collection-delay", ref delay))
        {
            settings.CollectionDelayMinutes = Math.Max(0, delay);
            changed = true;
        }

        EndSettingsCard();
        if (changed)
            this.draftDirty = true;
    }

    private void DrawRouteSettings()
    {
        var settings = this.draftSettings;
        var changed = false;
        BeginSettingsCard("route-card", "Voyage strategy", "Constrain route length and decide how unlock opportunities affect leveling.");

        if (settings.EtaModel == EtaModel.PracticalLeveling)
        {
            SettingLabel("Maximum voyage duration", "Exclude practical routes longer than this duration. No cap considers every valid route.");
            changed |= DrawPracticalDuration(settings);
        }
        else
        {
            var durationLimit = settings.DurationLimitHours;
            SettingLabel("Maximum voyage duration", "Set zero to consider routes of any duration.");
            ImGui.SetNextItemWidth(150f * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("Hours##duration-limit", ref durationLimit))
            {
                settings.DurationLimitHours = Math.Max(0, durationLimit);
                changed = true;
            }
        }

        var prioritizeSlots = settings.PrioritizeSubSlots;
        SettingLabel("Submarine slots", "Prefer objectives that unlock missing fleet slots before pure leveling routes.");
        if (ImGui.Checkbox("Prioritize missing submarine slots##prioritize-slots", ref prioritizeSlots))
        {
            settings.PrioritizeSubSlots = prioritizeSlots;
            changed = true;
        }

        var unlockChancePercent = (float)(settings.UnlockSuccessProbability * 100.0);
        SettingLabel(
            "Unlock chance per visit",
            "Community-informed probability used for each eligible sector-discovery roll. Square Enix does not publish an exact rate.");
        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("##unlock-probability", ref unlockChancePercent, 1f, 100f, "%.0f%%"))
        {
            settings.UnlockSuccessProbability = Math.Clamp(unlockChancePercent / 100.0, 0.01, 1.0);
            changed = true;
        }
        PlannerUi.Tooltip("Default: 33%. This assumption affects unlock timing and the displayed P10-P90 ETA range.");

        var unknownPolicy = settings.UnknownCurrentVoyagePolicy;
        SettingLabel("Unknown current voyage", "Choose how the planner handles a deployed submarine whose route cannot be identified.");
        if (DrawEnumCombo("##unknown-voyage", UnknownVoyageLabels, ref unknownPolicy))
        {
            settings.UnknownCurrentVoyagePolicy = unknownPolicy;
            changed = true;
        }

        EndSettingsCard();
        if (changed)
            this.draftDirty = true;
    }

    private void DrawLimitSettings()
    {
        var settings = this.draftSettings;
        var changed = false;
        BeginSettingsCard("limit-card", "Safety limits", "Bound each FC independently so one difficult fleet cannot block every other forecast.");

        var timeLimit = settings.CalculationTimeLimitSeconds;
        SettingLabel("Per-FC time limit", "Move to the next FC after this many seconds. Set zero for no deadline.");
        ImGui.SetNextItemWidth(150f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("Seconds##time-limit", ref timeLimit))
        {
            settings.CalculationTimeLimitSeconds = Math.Clamp(timeLimit, 0, 300);
            changed = true;
        }

        var safetyCap = settings.SimulationSafetyVoyageCapPerSubmarine;
        SettingLabel("Voyage safety cap", "Maximum simulated voyages for a single submarine before the result is marked incomplete.");
        ImGui.SetNextItemWidth(150f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("Voyages##safety-cap", ref safetyCap))
        {
            settings.SimulationSafetyVoyageCapPerSubmarine = Math.Clamp(safetyCap, 1, 5000);
            changed = true;
        }

        EndSettingsCard();
        if (changed)
            this.draftDirty = true;
    }

    private void DrawDataSourceSettings()
    {
        var settings = this.draftSettings;
        BeginSettingsCard("data-card", "Database location", "The default path is pluginConfigs\\SubmarineTracker\\submarine-sqlite.db under XIVLauncher.");

        var dbPath = settings.SubmarineTrackerDatabasePathOverride ?? string.Empty;
        SettingLabel("Database override", "Leave empty to use the standard SubmarineTracker location.");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##database-path", "Use the default SubmarineTracker database", ref dbPath, 512))
        {
            settings.SubmarineTrackerDatabasePathOverride = string.IsNullOrWhiteSpace(dbPath) ? null : dbPath.Trim();
            this.draftDirty = true;
        }

        PlannerUi.Callout(
            "data-safety",
            FontAwesomeIcon.InfoCircle,
            "Forecast data source",
            "The planner uses Submarine Tracker state to calculate voyage and leveling forecasts.",
            PlannerUi.Teal);
        EndSettingsCard();
    }

    private void DrawBuildProfileSettings()
    {
        BeginSettingsCard("build-card", "Rank-based builds", "Each rank should resolve to one four-letter submarine build code.");
        if (DrawBuildProfile(this.draftSettings))
            this.draftDirty = true;
        EndSettingsCard();
    }

    private void DrawDisplaySettings()
    {
        var previewingReset = this.resetDefaultsPreviewActive;
        BeginSettingsCard(
            "display-card",
            "Result presentation",
            previewingReset
                ? "Review the staged display defaults. They will be saved with Save changes."
                : "Saved automatically. Display preferences do not restart the forecast.");

        var showDiagnostics = previewingReset
            ? this.draftSettings.ShowRouteDiagnostics
            : this.configuration.Settings.ShowRouteDiagnostics;
        SettingLabel("Route diagnostics", "Show per-voyage duration, EXP, and EXP/hour columns in expanded forecasts.");
        if (ImGui.Checkbox("Show route diagnostics##show-diagnostics", ref showDiagnostics))
        {
            this.draftSettings.ShowRouteDiagnostics = showDiagnostics;
            if (previewingReset)
            {
                this.draftDirty = true;
            }
            else
            {
                this.configuration.Settings.ShowRouteDiagnostics = showDiagnostics;
                this.saveConfiguration();
            }
        }

        var timeoutBehavior = previewingReset
            ? this.draftSettings.TimeoutResultBehavior
            : this.configuration.Settings.TimeoutResultBehavior;
        SettingLabel("Timeout result", "Keep the last complete forecast or replace it with the newest partial result.");
        if (DrawEnumCombo("##timeout-behavior", TimeoutBehaviorLabels, ref timeoutBehavior))
        {
            this.draftSettings.TimeoutResultBehavior = timeoutBehavior;
            if (previewingReset)
            {
                this.draftDirty = true;
            }
            else
            {
                this.configuration.Settings.TimeoutResultBehavior = timeoutBehavior;
                this.saveConfiguration();
            }
        }

        EndSettingsCard();
    }

    private void DrawSettingsActionBar()
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, PlannerUi.PanelBackground);
        if (ImGui.BeginChild("settings-action-bar", new Vector2(-1, -1), true))
        {
            PlannerUi.WrappedText(this.draftDirty ? "Unsaved global changes" : "Global settings saved",
                this.draftDirty ? PlannerUi.Amber : PlannerUi.Muted);
            ImGui.BeginDisabled(!this.draftDirty);
            if (ImGui.Button("Save changes##apply-settings")) ApplyDraftSettings();
            PlannerUi.SameLineIfFits("Discard changes");
            if (ImGui.Button("Discard changes##revert-settings"))
            {
                this.draftSettings = CloneSettings(this.configuration.Settings);
                this.draftDirty = false;
                this.resetDefaultsPreviewActive = false;
            }
            ImGui.EndDisabled();
            PlannerUi.SameLineIfFits("Reset defaults");
            if (ImGui.Button("Reset defaults##reset-settings"))
                ImGui.OpenPopup("Reset all settings?###reset-all-settings");
            PlannerUi.WrappedText("Saving global calculation settings refreshes forecasts.", PlannerUi.Muted);
            DrawResetDefaultsModal();
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawResetDefaultsModal()
    {
        ImGui.SetNextWindowSize(new Vector2(520f * ImGuiHelpers.GlobalScale, 0), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(
                "Reset all settings?###reset-all-settings",
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            return;
        }

        ImGui.TextWrapped(
            "This loads defaults for Simulation, Routes, Limits, Data Source, Build Profile, and Display—not only the page currently shown.");
        ImGui.Spacing();
        ImGui.TextColored(PlannerUi.Amber, "Unapplied edits and custom data-source or route overrides will be replaced in the draft.");
        ImGui.TextWrapped("Nothing is saved or recalculated until you select Save changes. You can inspect every tab or use Discard changes first.");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (PlannerUi.IconButtonWithText("cancel-reset-settings", FontAwesomeIcon.Times, "Cancel"))
            ImGui.CloseCurrentPopup();
        ImGui.SetItemDefaultFocus();
        ImGui.SameLine();
        if (PlannerUi.IconButtonWithText("confirm-reset-settings", FontAwesomeIcon.SyncAlt, "Load defaults for review"))
        {
            this.draftSettings = EtaSettings.CreateDefault();
            this.draftDirty = true;
            this.resetDefaultsPreviewActive = true;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private static bool DrawPracticalDuration(EtaSettings settings)
    {
        var current = Array.IndexOf(PracticalDurations, settings.PracticalMaxVoyageHours);
        if (current < 0)
            current = PracticalDurations.Length - 1;
        var changed = false;

        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("##practical-duration", PracticalDurationLabels[current]))
        {
            for (var i = 0; i < PracticalDurationLabels.Length; i++)
            {
                if (ImGui.Selectable(PracticalDurationLabels[i], i == current))
                {
                    settings.PracticalMaxVoyageHours = PracticalDurations[i] < 0
                        ? (settings.PracticalMaxVoyageHours is 0 or 24 or 36 or 48 ? 42 : settings.PracticalMaxVoyageHours)
                        : PracticalDurations[i];
                    current = i;
                    changed = true;
                }
            }
            ImGui.EndCombo();
        }

        if (current == PracticalDurations.Length - 1)
        {
            var custom = Math.Max(1, settings.PracticalMaxVoyageHours);
            ImGui.SetNextItemWidth(150f * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("Hours##custom-duration", ref custom))
            {
                settings.PracticalMaxVoyageHours = Math.Clamp(custom, 1, 168);
                changed = true;
            }
        }

        return changed;
    }

    private static bool DrawBuildProfile(EtaSettings settings)
    {
        if (settings.BuildProfile.Count == 0)
            settings.BuildProfile = EtaSettings.CreateDefault().BuildProfile;

        var changed = false;
        var removeIndex = -1;
        if (ImGui.BeginTable("build-profile", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Minimum rank", ImGuiTableColumnFlags.WidthStretch, 0.2f);
            ImGui.TableSetupColumn("Maximum rank", ImGuiTableColumnFlags.WidthStretch, 0.2f);
            ImGui.TableSetupColumn("Build code", ImGuiTableColumnFlags.WidthStretch, 0.5f);
            ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.WidthFixed, 48f * ImGuiHelpers.GlobalScale);
            ImGui.TableHeadersRow();
            for (var i = 0; i < settings.BuildProfile.Count; i++)
            {
                var step = settings.BuildProfile[i];
                var min = step.MinRank;
                var max = step.MaxRank;
                var build = step.BuildCode;
                ImGui.PushID(i);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                var rowChanged = ImGui.InputInt("##min", ref min);
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                rowChanged |= ImGui.InputInt("##max", ref max);
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                rowChanged |= ImGui.InputText("##build", ref build, 16);
                ImGui.TableNextColumn();
                if (PlannerUi.IconButton("remove-build", FontAwesomeIcon.Trash, "Remove this build range"))
                    removeIndex = i;

                if (rowChanged)
                {
                    settings.BuildProfile[i] = new BuildProfileStep(
                        Math.Clamp(min, 1, 999),
                        Math.Clamp(max, 1, 999),
                        NormalizeBuildCode(build));
                    changed = true;
                }
                ImGui.PopID();
            }
            ImGui.EndTable();
        }

        if (removeIndex >= 0)
        {
            settings.BuildProfile.RemoveAt(removeIndex);
            changed = true;
        }

        if (PlannerUi.IconButtonWithText("add-build-range", FontAwesomeIcon.Plus, "Add range"))
        {
            var nextRank = settings.BuildProfile.Count == 0
                ? 1
                : Math.Clamp(settings.BuildProfile.Max(step => step.MaxRank) + 1, 1, 999);
            settings.BuildProfile.Add(new BuildProfileStep(nextRank, 999, "SSSS"));
            changed = true;
        }
        ImGui.SameLine();
        if (PlannerUi.IconButtonWithText("reset-build-profile", FontAwesomeIcon.Undo, "Reset profile"))
        {
            settings.BuildProfile = EtaSettings.CreateDefault().BuildProfile;
            changed = true;
        }

        return changed;
    }

    private static bool DrawEnumCombo<TEnum>(string label, IReadOnlyList<string> labels, ref TEnum value, float width = 360f)
        where TEnum : struct, Enum
    {
        var current = Math.Clamp(Convert.ToInt32(value), 0, labels.Count - 1);
        var changed = false;
        ImGui.SetNextItemWidth(Math.Min(width * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        if (!ImGui.BeginCombo(label, labels[current]))
            return false;

        for (var i = 0; i < labels.Count; i++)
        {
            if (ImGui.Selectable(labels[i], i == current))
            {
                value = (TEnum)Enum.ToObject(typeof(TEnum), i);
                changed = true;
            }
        }
        ImGui.EndCombo();
        return changed;
    }

    private readonly record struct ContentPanelFrame(
        ImDrawListPtr DrawList,
        Vector2 Start,
        float Width,
        float Padding);

    private static readonly Stack<ContentPanelFrame> ContentPanelFrames = [];

    private static void BeginContentPanel(string id)
    {
        var start = ImGui.GetCursorScreenPos();
        var width = Math.Max(1f, ImGui.GetContentRegionAvail().X);
        var padding = 10f * ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);
        ImGui.PushID(id);
        ImGui.BeginGroup();
        ImGui.Dummy(new Vector2(0, padding));
        ImGui.Indent(padding);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - padding);
        ContentPanelFrames.Push(new ContentPanelFrame(drawList, start, width, padding));
    }

    private static void EndContentPanel()
    {
        var frame = ContentPanelFrames.Pop();
        ImGui.PopTextWrapPos();
        ImGui.Unindent(frame.Padding);
        ImGui.Dummy(new Vector2(0, frame.Padding));
        ImGui.EndGroup();
        var endY = ImGui.GetItemRectMax().Y;
        frame.DrawList.ChannelsSetCurrent(0);
        frame.DrawList.AddRectFilled(
            frame.Start,
            new Vector2(frame.Start.X + frame.Width, endY),
            ImGui.ColorConvertFloat4ToU32(PlannerUi.PanelBackground),
            6f * ImGuiHelpers.GlobalScale);
        frame.DrawList.AddRect(
            frame.Start,
            new Vector2(frame.Start.X + frame.Width, endY),
            ImGui.ColorConvertFloat4ToU32(PlannerUi.Border),
            6f * ImGuiHelpers.GlobalScale);
        frame.DrawList.ChannelsSetCurrent(1);
        frame.DrawList.ChannelsMerge();
        ImGui.PopID();
    }

    private static void BeginSettingsCard(string id, string title, string description)
    {
        BeginContentPanel(id);
        ImGui.TextColored(PlannerUi.Teal, title);
        PlannerUi.WrappedText(description, PlannerUi.Muted);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    private static void EndSettingsCard()
    {
        EndContentPanel();
    }

    private static void SettingLabel(string label, string help)
    {
        ImGui.TextUnformatted(label);
        PlannerUi.WrappedText(help, PlannerUi.Muted);
        ImGui.Spacing();
    }
}
