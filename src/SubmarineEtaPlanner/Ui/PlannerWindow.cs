using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using SubmarineEtaPlanner.Planner;

namespace SubmarineEtaPlanner.Ui;

public sealed class PlannerWindow : Window
{
    private readonly Configuration configuration;
    private readonly Action saveConfiguration;
    private readonly EtaPlannerService plannerService;
    private EtaPlannerSnapshot? snapshot;
    private string lastError = string.Empty;

    public PlannerWindow(Configuration configuration, Action saveConfiguration, EtaPlannerService plannerService)
        : base("Submarine ETA Planner###SubmarineEtaPlanner")
    {
        this.configuration = configuration;
        this.saveConfiguration = saveConfiguration;
        this.plannerService = plannerService;
        IsOpen = configuration.WindowOpen;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new System.Numerics.Vector2(760, 460),
            MaximumSize = new System.Numerics.Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void OnClose()
    {
        this.configuration.WindowOpen = false;
        this.saveConfiguration();
    }

    public override void Draw()
    {
        DrawToolbar();

        if (!string.IsNullOrWhiteSpace(this.lastError))
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.35f, 0.25f, 1f), this.lastError);

        this.snapshot ??= CalculateSnapshot();

        if (this.snapshot.Warnings.Count > 0)
        {
            DrawSectionHeader("Warnings");
            foreach (var warning in this.snapshot.Warnings)
                ImGui.BulletText(warning);
        }

        DrawSectionHeader("FC Summary");
        if (this.snapshot.Results.Count == 0)
        {
            ImGui.TextUnformatted("No SubmarineTracker data was found.");
            return;
        }

        foreach (var fcResult in this.snapshot.Results)
            DrawFcResult(fcResult);
    }

    private void DrawToolbar()
    {
        if (ImGui.Button("Refresh"))
            this.snapshot = CalculateSnapshot();

        ImGui.SameLine();
        var target = this.configuration.Settings.TargetRank;
        ImGui.SetNextItemWidth(90);
        if (ImGui.InputInt("Target rank", ref target))
        {
            this.configuration.Settings.TargetRank = Math.Clamp(target, 1, 149);
            this.saveConfiguration();
            this.snapshot = null;
        }

        ImGui.SameLine();
        var fleetMode = this.configuration.Settings.SimulationMode == SimulationMode.Fleet;
        if (ImGui.Checkbox("Fleet mode", ref fleetMode))
        {
            this.configuration.Settings.SimulationMode = fleetMode ? SimulationMode.Fleet : SimulationMode.OptimisticPerSub;
            this.saveConfiguration();
            this.snapshot = null;
        }

        ImGui.SameLine();
        var averageExp = this.configuration.Settings.ExpMode == ExpMode.Average;
        if (ImGui.Checkbox("Average EXP", ref averageExp))
        {
            this.configuration.Settings.ExpMode = averageExp ? ExpMode.Average : ExpMode.Guaranteed;
            this.saveConfiguration();
            this.snapshot = null;
        }

        if (ImGui.CollapsingHeader("Settings"))
            DrawSettings();
    }

    private void DrawSettings()
    {
        var delay = this.configuration.Settings.CollectionDelayMinutes;
        if (ImGui.InputInt("Collection delay minutes", ref delay))
        {
            this.configuration.Settings.CollectionDelayMinutes = Math.Max(0, delay);
            this.saveConfiguration();
            this.snapshot = null;
        }

        var durationLimit = this.configuration.Settings.DurationLimitHours;
        if (ImGui.InputInt("Duration limit hours", ref durationLimit))
        {
            this.configuration.Settings.DurationLimitHours = Math.Max(0, durationLimit);
            this.saveConfiguration();
            this.snapshot = null;
        }

        var prioritizeSlots = this.configuration.Settings.PrioritizeSubSlots;
        if (ImGui.Checkbox("Prioritize sub slot unlocks", ref prioritizeSlots))
        {
            this.configuration.Settings.PrioritizeSubSlots = prioritizeSlots;
            this.saveConfiguration();
            this.snapshot = null;
        }

        var showReadiness = this.configuration.Settings.ShowPost114MrojzReadiness;
        if (ImGui.Checkbox("Show post-114 MROJZ readiness", ref showReadiness))
        {
            this.configuration.Settings.ShowPost114MrojzReadiness = showReadiness;
            this.saveConfiguration();
            this.snapshot = null;
        }

        var dbPath = this.configuration.Settings.SubmarineTrackerDatabasePathOverride ?? string.Empty;
        ImGui.SetNextItemWidth(520);
        if (ImGui.InputText("SubmarineTracker DB override", ref dbPath, 512))
        {
            this.configuration.Settings.SubmarineTrackerDatabasePathOverride = string.IsNullOrWhiteSpace(dbPath) ? null : dbPath;
            this.saveConfiguration();
            this.snapshot = null;
        }

        ImGui.TextUnformatted("Build profile: 1-14 SSSS, 15-24 SSUS, 25-113 SSUW, 114+ WSCC.");
        ImGui.TextUnformatted("Estimator only. No deployment, collection, UI clicking, or automation is performed.");
    }

    private void DrawFcResult(EtaResult result)
    {
        if (!ImGui.CollapsingHeader($"{result.FcDisplayName} - done {FormatRelative(result.FcCompletionAtUtc, result.GeneratedAtUtc)}###fc-{Convert.ToHexString(result.FcId)}", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        if (!ImGui.BeginTable($"table-{Convert.ToHexString(result.FcId)}", 8, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            return;

        ImGui.TableSetupColumn("Submarine");
        ImGui.TableSetupColumn("Rank");
        ImGui.TableSetupColumn("ETA");
        ImGui.TableSetupColumn("Voyages");
        ImGui.TableSetupColumn("Build");
        ImGui.TableSetupColumn("Next route");
        ImGui.TableSetupColumn("Ready");
        ImGui.TableSetupColumn("Warnings");
        ImGui.TableHeadersRow();

        foreach (var sub in result.PerSubResults.OrderBy(r => r.SubmarineName))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var open = ImGui.TreeNodeEx($"{sub.SubmarineName}###sub-{sub.SubmarineId}", ImGuiTreeNodeFlags.SpanFullWidth);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{sub.StartingRank} -> {sub.FinalRank}");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatRelative(sub.EtaAtUtc, result.GeneratedAtUtc));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(sub.VoyageCount.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(sub.PlannedBuild);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatRoute(sub.NextRoute));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(sub.PostTargetFarmingReady ? "WSCC/MROJZ" : "-");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(sub.Warnings.Count.ToString());

            if (open)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TableSetColumnIndex(0);
                DrawSubDetails(sub);
                ImGui.TreePop();
            }
        }

        ImGui.EndTable();
    }

    private static void DrawSubDetails(PerSubEtaResult sub)
    {
        ImGui.Indent();
        if (sub.Warnings.Count > 0)
        {
            ImGui.TextUnformatted("Warnings");
            foreach (var warning in sub.Warnings)
                ImGui.BulletText(warning);
        }

        if (sub.UnlockMilestones.Count > 0)
        {
            ImGui.TextUnformatted("Unlock milestones");
            foreach (var milestone in sub.UnlockMilestones)
                ImGui.BulletText($"{milestone.SourcePoint} unlocked {milestone.UnlockedPoint} at {milestone.ReturnAtUtc.LocalDateTime:g}");
        }

        if (ImGui.BeginTable($"preview-{sub.SubmarineId}", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("#");
            ImGui.TableSetupColumn("Return");
            ImGui.TableSetupColumn("Build");
            ImGui.TableSetupColumn("Route");
            ImGui.TableSetupColumn("EXP");
            ImGui.TableSetupColumn("Rank");
            ImGui.TableHeadersRow();

            for (var i = 0; i < sub.VoyagePreview.Count; i++)
            {
                var plan = sub.VoyagePreview[i];
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted((i + 1).ToString());
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(plan.ReturnAtUtc.LocalDateTime.ToString("g"));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(plan.BuildCode);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(FormatRoute(plan.Route));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(plan.ExpGain.ToString("N0"));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{plan.RankBefore}->{plan.RankAfter}");
            }

            ImGui.EndTable();
        }

        ImGui.Unindent();
    }

    private EtaPlannerSnapshot CalculateSnapshot()
    {
        try
        {
            this.lastError = string.Empty;
            return this.plannerService.Calculate(this.configuration.Settings, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            this.lastError = ex.Message;
            return new EtaPlannerSnapshot(DateTimeOffset.UtcNow, [], [], [ex.Message]);
        }
    }

    private static string FormatRelative(DateTimeOffset date, DateTimeOffset now)
    {
        var remaining = date - now;
        if (remaining <= TimeSpan.Zero)
            return "now";

        return remaining.TotalDays >= 1
            ? $"{(int)remaining.TotalDays}d {remaining.Hours}h"
            : $"{remaining.Hours}h {remaining.Minutes}m";
    }

    private static string FormatRoute(IReadOnlyList<uint> route) => route.Count == 0 ? "-" : string.Join("-", route);

    private static void DrawSectionHeader(string label)
    {
        ImGui.Separator();
        ImGui.TextUnformatted(label);
    }
}
