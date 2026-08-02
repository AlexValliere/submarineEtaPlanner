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
        switch (this.currentPage)
        {
            case PlannerPage.Simulation:
                DrawSimulationSettings();
                break;
            case PlannerPage.Routes:
                DrawRouteSettings();
                break;
            case PlannerPage.Limits:
                DrawLimitSettings();
                break;
            case PlannerPage.DataSource:
                DrawDataSourceSettings();
                break;
            case PlannerPage.BuildProfile:
                DrawBuildProfileSettings();
                break;
            case PlannerPage.Display:
                DrawDisplaySettings();
                break;
        }
    }

    private void DrawSimulationSettings()
    {
        var settings = this.draftSettings;
        var changed = false;
        BeginSettingsCard("simulation-card", "Forecast model", "These settings define the target and how submarine fleets advance toward it.");

        var etaModel = settings.EtaModel;
        SettingLabel("ETA model", "Practical leveling follows proven progression rules; exact search exposes advanced route controls.");
        if (DrawEnumCombo("##eta-model", EtaModelLabels, ref etaModel))
        {
            settings.EtaModel = etaModel;
            changed = true;
        }

        var target = settings.TargetRank;
        SettingLabel("Target rank", "The rank every tracked submarine should reach.");
        ImGui.SetNextItemWidth(150f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("##target-rank", ref target))
        {
            settings.TargetRank = Math.Clamp(target, 1, 149);
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
                "Practical model defaults",
                "Average EXP  •  maximum total EXP scoring  •  main leveling-route unlock progression",
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
            SettingLabel("Route goal", "Choose which unlock objective exact route search should pursue.");
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
        BeginSettingsCard("limit-card", "Safety and preview limits", "Protect the game thread from pathological searches while keeping useful forecast detail.");

        var timeLimit = settings.CalculationTimeLimitSeconds;
        SettingLabel("Calculation time limit", "Stop route search after this many seconds. Set zero for no deadline.");
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

        var previewCount = settings.MaxPreviewVoyagesPerSubmarine;
        SettingLabel("Preview rows", "Limit the number of voyage-plan rows shown in expanded submarine details.");
        ImGui.SetNextItemWidth(150f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("Rows##preview-count", ref previewCount))
        {
            settings.MaxPreviewVoyagesPerSubmarine = Math.Clamp(previewCount, 1, 100);
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
            "Read-only integration",
            "The planner reads tracker state only. It never deploys submarines, changes routes, or automates workshop actions.",
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
        BeginSettingsCard("display-card", "Result presentation", "Display preferences save immediately and do not restart the forecast.");

        var showDiagnostics = this.configuration.Settings.ShowRouteDiagnostics;
        SettingLabel("Route diagnostics", "Show per-voyage duration, EXP, and EXP/hour columns in expanded forecasts.");
        if (ImGui.Checkbox("Show route diagnostics##show-diagnostics", ref showDiagnostics))
        {
            this.configuration.Settings.ShowRouteDiagnostics = showDiagnostics;
            this.draftSettings.ShowRouteDiagnostics = showDiagnostics;
            this.saveConfiguration();
        }

        var showReadiness = this.configuration.Settings.ShowPost114MrojzReadiness;
        SettingLabel("Post-target readiness", "Show whether the planned WSCC build is ready for MROJZ farming after rank 114.");
        if (ImGui.Checkbox("Show post-114 MROJZ readiness##show-readiness", ref showReadiness))
        {
            this.configuration.Settings.ShowPost114MrojzReadiness = showReadiness;
            this.draftSettings.ShowPost114MrojzReadiness = showReadiness;
            this.saveConfiguration();
        }

        var timeoutBehavior = this.configuration.Settings.TimeoutResultBehavior;
        SettingLabel("Timeout result", "Keep the last complete forecast or replace it with the newest partial result.");
        if (DrawEnumCombo("##timeout-behavior", TimeoutBehaviorLabels, ref timeoutBehavior))
        {
            this.configuration.Settings.TimeoutResultBehavior = timeoutBehavior;
            this.draftSettings.TimeoutResultBehavior = timeoutBehavior;
            this.saveConfiguration();
        }

        EndSettingsCard();
    }

    private void DrawSettingsActionBar()
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, PlannerUi.PanelBackground);
        if (ImGui.BeginChild("settings-action-bar", new Vector2(-1, 58f * ImGuiHelpers.GlobalScale), true, ImGuiWindowFlags.NoScrollbar))
        {
            if (this.draftDirty)
            {
                PlannerUi.DrawStatusPill("Unapplied changes", PlannerUi.Amber);
                ImGui.SameLine();
            }
            else
            {
                PlannerUi.DrawStatusPill("Settings up to date", PlannerUi.Green);
                ImGui.SameLine();
            }

            var actionsDisabled = !this.draftDirty;
            if (actionsDisabled)
                ImGui.BeginDisabled();
            if (ImGui.Button($"{FontAwesomeIcon.Check.ToIconString()}  Apply & refresh"))
                ApplyDraftSettings();
            ImGui.SameLine();
            if (ImGui.Button($"{FontAwesomeIcon.Undo.ToIconString()}  Revert"))
            {
                this.draftSettings = CloneSettings(this.configuration.Settings);
                this.draftDirty = false;
            }
            if (actionsDisabled)
                ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button($"{FontAwesomeIcon.SyncAlt.ToIconString()}  Reset defaults"))
            {
                this.draftSettings = EtaSettings.CreateDefault();
                this.draftSettings.ShowRouteDiagnostics = this.configuration.Settings.ShowRouteDiagnostics;
                this.draftSettings.ShowPost114MrojzReadiness = this.configuration.Settings.ShowPost114MrojzReadiness;
                this.draftSettings.TimeoutResultBehavior = this.configuration.Settings.TimeoutResultBehavior;
                this.draftDirty = true;
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
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

        if (ImGui.Button($"{FontAwesomeIcon.Plus.ToIconString()}  Add range"))
        {
            settings.BuildProfile.Add(new BuildProfileStep(114, 999, "WSCC"));
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button($"{FontAwesomeIcon.Undo.ToIconString()}  Reset profile"))
        {
            settings.BuildProfile = EtaSettings.CreateDefault().BuildProfile;
            changed = true;
        }

        return changed;
    }

    private static bool DrawEnumCombo<TEnum>(string label, IReadOnlyList<string> labels, ref TEnum value)
        where TEnum : struct, Enum
    {
        var current = Math.Clamp(Convert.ToInt32(value), 0, labels.Count - 1);
        var changed = false;
        ImGui.SetNextItemWidth(Math.Min(360f * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
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

    private static void BeginSettingsCard(string id, string title, string description)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, PlannerUi.PanelBackground);
        ImGui.PushStyleColor(ImGuiCol.Border, PlannerUi.Border);
        ImGui.BeginChild(id, new Vector2(-1, 0), true);
        ImGui.TextColored(PlannerUi.Teal, title);
        ImGui.TextColored(PlannerUi.Muted, description);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    private static void EndSettingsCard()
    {
        ImGui.EndChild();
        ImGui.PopStyleColor(2);
    }

    private static void SettingLabel(string label, string help)
    {
        ImGui.TextUnformatted(label);
        ImGui.TextColored(PlannerUi.Muted, help);
        ImGui.Spacing();
    }
}
