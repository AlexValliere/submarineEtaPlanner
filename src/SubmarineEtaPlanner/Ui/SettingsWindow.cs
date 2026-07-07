using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using SubmarineEtaPlanner.Planner;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

public sealed class SettingsWindow : Window
{
    private static readonly string[] UnknownVoyageLabels =
    [
        "Warn and ignore",
        "Block simulation",
        "Manual override",
    ];

    private static readonly string[] RouteGoalLabels =
    [
        "Fastest leveling only",
        "Unlock sub slots then level",
        "Unlock everything then level",
    ];

    private static readonly string[] EtaModelLabels =
    [
        "Practical leveling",
        "Exact route search",
    ];

    private static readonly string[] TimeoutBehaviorLabels =
    [
        "Keep last complete",
        "Show partial",
    ];

    private readonly Configuration configuration;
    private readonly Action saveConfiguration;
    private readonly Action settingsChanged;

    public SettingsWindow(Configuration configuration, Action saveConfiguration, Action settingsChanged)
        : base("Submarine ETA Planner Settings###SubmarineEtaPlannerSettings")
    {
        this.configuration = configuration;
        this.saveConfiguration = saveConfiguration;
        this.settingsChanged = settingsChanged;
        IsOpen = false;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 380),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        var settings = this.configuration.Settings;
        var changed = false;

        DrawSectionHeader("Simulation");

        DrawEtaModel(settings, ref changed);

        var target = settings.TargetRank;
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("Target rank", ref target))
        {
            settings.TargetRank = Math.Clamp(target, 1, 149);
            changed = true;
        }

        var collectionDelay = settings.CollectionDelayMinutes;
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("Collection delay minutes", ref collectionDelay))
        {
            settings.CollectionDelayMinutes = Math.Max(0, collectionDelay);
            changed = true;
        }

        var durationLimit = settings.DurationLimitHours;
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("Duration limit hours", ref durationLimit))
        {
            settings.DurationLimitHours = Math.Max(0, durationLimit);
            changed = true;
        }

        var practicalLimit = settings.PracticalMaxVoyageHours;
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("Practical max voyage hours", ref practicalLimit))
        {
            settings.PracticalMaxVoyageHours = Math.Clamp(practicalLimit, 1, 168);
            changed = true;
        }

        var safetyCap = settings.SimulationSafetyVoyageCapPerSubmarine;
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("Voyage safety cap", ref safetyCap))
        {
            settings.SimulationSafetyVoyageCapPerSubmarine = Math.Clamp(safetyCap, 1, 5000);
            changed = true;
        }

        var timeLimit = settings.CalculationTimeLimitSeconds;
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("Calculation time limit seconds", ref timeLimit))
        {
            settings.CalculationTimeLimitSeconds = Math.Clamp(timeLimit, 0, 300);
            changed = true;
        }

        var fleetMode = settings.SimulationMode == SimulationMode.Fleet;
        if (ImGui.Checkbox("Fleet simulation", ref fleetMode))
        {
            settings.SimulationMode = fleetMode ? SimulationMode.Fleet : SimulationMode.OptimisticPerSub;
            changed = true;
        }

        var averageExp = settings.ExpMode == ExpMode.Average;
        if (ImGui.Checkbox("Average EXP", ref averageExp))
        {
            settings.ExpMode = averageExp ? ExpMode.Average : ExpMode.Guaranteed;
            changed = true;
        }

        var optimizeExpPerHour = settings.OptimizeExpPerHour;
        if (ImGui.Checkbox("Optimize EXP/hour", ref optimizeExpPerHour))
        {
            settings.OptimizeExpPerHour = optimizeExpPerHour;
            changed = true;
        }

        DrawRouteGoal(settings, ref changed);
        DrawTimeoutBehavior(settings, ref changed);

        var prioritizeSlots = settings.PrioritizeSubSlots;
        if (ImGui.Checkbox("Prioritize sub slot unlocks", ref prioritizeSlots))
        {
            settings.PrioritizeSubSlots = prioritizeSlots;
            changed = true;
        }

        var showReadiness = settings.ShowPost114MrojzReadiness;
        if (ImGui.Checkbox("Show post-114 MROJZ readiness", ref showReadiness))
        {
            settings.ShowPost114MrojzReadiness = showReadiness;
            changed = true;
        }

        var showDiagnostics = settings.ShowRouteDiagnostics;
        if (ImGui.Checkbox("Show route diagnostics", ref showDiagnostics))
        {
            settings.ShowRouteDiagnostics = showDiagnostics;
            changed = true;
        }

        DrawUnknownVoyagePolicy(settings, ref changed);

        DrawSectionHeader("SubmarineTracker");

        var dbPath = settings.SubmarineTrackerDatabasePathOverride ?? string.Empty;
        ImGui.SetNextItemWidth(Math.Min(650f, ImGui.GetContentRegionAvail().X));
        if (ImGui.InputText("DB override", ref dbPath, 512))
        {
            settings.SubmarineTrackerDatabasePathOverride = string.IsNullOrWhiteSpace(dbPath) ? null : dbPath.Trim();
            changed = true;
        }

        DrawSectionHeader("Build Profile");
        DrawBuildProfile(settings, ref changed);

        if (changed)
        {
            this.saveConfiguration();
            this.settingsChanged();
        }
    }

    private static void DrawEtaModel(EtaSettings settings, ref bool changed)
    {
        var current = (int)settings.EtaModel;
        var label = EtaModelLabels[Math.Clamp(current, 0, EtaModelLabels.Length - 1)];
        if (!ImGui.BeginCombo("ETA model", label))
            return;

        for (var i = 0; i < EtaModelLabels.Length; i++)
        {
            var selected = i == current;
            if (ImGui.Selectable(EtaModelLabels[i], selected))
            {
                settings.EtaModel = (EtaModel)i;
                changed = true;
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private static void DrawRouteGoal(EtaSettings settings, ref bool changed)
    {
        var current = (int)settings.RouteGoal;
        var label = RouteGoalLabels[Math.Clamp(current, 0, RouteGoalLabels.Length - 1)];
        if (!ImGui.BeginCombo("Route goal", label))
            return;

        for (var i = 0; i < RouteGoalLabels.Length; i++)
        {
            var selected = i == current;
            if (ImGui.Selectable(RouteGoalLabels[i], selected))
            {
                settings.RouteGoal = (RouteGoal)i;
                changed = true;
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private static void DrawUnknownVoyagePolicy(EtaSettings settings, ref bool changed)
    {
        var current = (int)settings.UnknownCurrentVoyagePolicy;
        var label = UnknownVoyageLabels[Math.Clamp(current, 0, UnknownVoyageLabels.Length - 1)];
        if (!ImGui.BeginCombo("Unknown current voyage", label))
            return;

        for (var i = 0; i < UnknownVoyageLabels.Length; i++)
        {
            var selected = i == current;
            if (ImGui.Selectable(UnknownVoyageLabels[i], selected))
            {
                settings.UnknownCurrentVoyagePolicy = (UnknownCurrentVoyagePolicy)i;
                changed = true;
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private static void DrawTimeoutBehavior(EtaSettings settings, ref bool changed)
    {
        var current = (int)settings.TimeoutResultBehavior;
        var label = TimeoutBehaviorLabels[Math.Clamp(current, 0, TimeoutBehaviorLabels.Length - 1)];
        if (!ImGui.BeginCombo("Timeout result", label))
            return;

        for (var i = 0; i < TimeoutBehaviorLabels.Length; i++)
        {
            var selected = i == current;
            if (ImGui.Selectable(TimeoutBehaviorLabels[i], selected))
            {
                settings.TimeoutResultBehavior = (TimeoutResultBehavior)i;
                changed = true;
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private static void DrawBuildProfile(EtaSettings settings, ref bool changed)
    {
        if (settings.BuildProfile.Count == 0)
        {
            settings.BuildProfile = EtaSettings.CreateDefault().BuildProfile;
            changed = true;
        }

        if (ImGui.BeginTable("build-profile-table", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Min");
            ImGui.TableSetupColumn("Max");
            ImGui.TableSetupColumn("Build");
            ImGui.TableHeadersRow();

            for (var i = 0; i < settings.BuildProfile.Count; i++)
            {
                var step = settings.BuildProfile[i];
                var minRank = step.MinRank;
                var maxRank = step.MaxRank;
                var buildCode = step.BuildCode;

                ImGui.PushID(i);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                var rowChanged = ImGui.InputInt("##min", ref minRank);
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                rowChanged |= ImGui.InputInt("##max", ref maxRank);
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                rowChanged |= ImGui.InputText("##build", ref buildCode, 16);

                if (rowChanged)
                {
                    settings.BuildProfile[i] = new BuildProfileStep(
                        Math.Clamp(minRank, 1, 999),
                        Math.Clamp(maxRank, 1, 999),
                        NormalizeBuildCode(buildCode));
                    changed = true;
                }

                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        if (ImGui.Button("Add step"))
        {
            settings.BuildProfile.Add(new BuildProfileStep(114, 999, "WSCC"));
            changed = true;
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset default"))
        {
            settings.BuildProfile = EtaSettings.CreateDefault().BuildProfile;
            changed = true;
        }
    }

    private static string NormalizeBuildCode(string value)
    {
        var normalized = new string((value ?? string.Empty).ToUpperInvariant().Where(char.IsLetter).Take(4).ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "SSSS" : normalized;
    }

    private static void DrawSectionHeader(string label)
    {
        ImGui.Separator();
        ImGui.TextUnformatted(label);
    }
}
