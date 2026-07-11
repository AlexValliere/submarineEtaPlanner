using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using SubmarineEtaPlanner.Planner;
using System.Diagnostics;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

public sealed class PlannerWindow : Window
{
    private static readonly string[] EtaModelLabels = ["Practical leveling", "Exact route search"];
    private static readonly string[] RouteGoalLabels =
    [
        "Fastest leveling only",
        "Unlock sub slots then level",
        "Unlock everything then level",
        "Unlock main leveling routes",
    ];
    private static readonly string[] UnknownVoyageLabels = ["Warn and ignore", "Block simulation", "Manual override"];
    private static readonly string[] TimeoutBehaviorLabels = ["Keep last complete", "Show partial"];
    private static readonly string[] PracticalDurationLabels = ["No cap", "24 hours", "36 hours", "48 hours", "Custom"];
    private static readonly int[] PracticalDurations = [0, 24, 36, 48, -1];

    private readonly Configuration configuration;
    private readonly Action saveConfiguration;
    private readonly EtaPlannerService plannerService;
    private readonly ResultsViewState viewState = new();
    private readonly HashSet<string> expandedSubmarines = [];
    private EtaPlannerSnapshot? snapshot;
    private Task<EtaPlannerSnapshot>? refreshTask;
    private CancellationTokenSource? refreshCancellation;
    private DateTimeOffset? refreshStartedAtUtc;
    private bool refreshPending;
    private string lastError = string.Empty;
    private string fcSearch = string.Empty;
    private PlannerTab requestedTab = PlannerTab.Results;
    private EtaSettings draftSettings;
    private bool draftDirty;

    public PlannerWindow(Configuration configuration, Action saveConfiguration, EtaPlannerService plannerService)
        : base("Submarine ETA Planner###SubmarineEtaPlanner")
    {
        this.configuration = configuration;
        this.saveConfiguration = saveConfiguration;
        this.plannerService = plannerService;
        this.draftSettings = CloneSettings(configuration.Settings);
        IsOpen = configuration.WindowOpen;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(780, 480),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void OnClose()
    {
        this.configuration.WindowOpen = false;
        this.refreshCancellation?.Cancel();
        this.saveConfiguration();
    }

    public void OpenResults()
    {
        this.requestedTab = PlannerTab.Results;
        this.configuration.WindowOpen = true;
        IsOpen = true;
    }

    public void OpenSettings()
    {
        if (!this.draftDirty)
            this.draftSettings = CloneSettings(this.configuration.Settings);
        this.requestedTab = PlannerTab.Settings;
        this.configuration.WindowOpen = true;
        IsOpen = true;
    }

    public void CancelRefresh() => this.refreshCancellation?.Cancel();

    public void InvalidateSnapshot()
    {
        if (!IsOpen)
        {
            this.snapshot = null;
            return;
        }

        QueueRefresh();
    }

    public override void Draw()
    {
        CompleteRefreshIfReady();
        if (!ImGui.BeginTabBar("planner-tabs"))
            return;

        var resultsFlags = this.requestedTab == PlannerTab.Results ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        if (ImGui.BeginTabItem("Results", resultsFlags))
        {
            DrawResultsTab();
            ImGui.EndTabItem();
        }

        var settingsFlags = this.requestedTab == PlannerTab.Settings ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        if (ImGui.BeginTabItem("Settings", settingsFlags))
        {
            DrawSettingsTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
        this.requestedTab = PlannerTab.None;
    }

    private void DrawResultsTab()
    {
        if (this.snapshot is null && this.refreshTask is null)
            StartRefresh();

        DrawResultsToolbar();

        if (!string.IsNullOrWhiteSpace(this.lastError))
            ImGui.TextColored(new Vector4(1f, 0.35f, 0.25f, 1f), this.lastError);

        if (this.refreshTask is { IsCompleted: false })
        {
            var elapsed = this.refreshStartedAtUtc is null
                ? TimeSpan.Zero
                : DateTimeOffset.UtcNow - this.refreshStartedAtUtc.Value;
            ImGui.TextUnformatted($"{(this.snapshot is null ? "Calculating" : "Refreshing")} ETA... {FormatElapsed(elapsed)}");
            if (this.snapshot is null)
                return;
        }

        var currentSnapshot = this.snapshot;
        if (currentSnapshot is null)
            return;

        if (currentSnapshot.Metrics is not null)
        {
            var metrics = currentSnapshot.Metrics;
            ImGui.TextDisabled(
                $"Calculated in {metrics.ElapsedMilliseconds:N0} ms | {metrics.RouteQueries:N0} route queries | " +
                $"{metrics.RouteCacheHits:N0} cache hits | {metrics.RoutesEvaluated:N0} routes checked");
        }

        if (currentSnapshot.Warnings.Count > 0 || !currentSnapshot.IsComplete)
        {
            if (ImGui.CollapsingHeader("Warnings"))
            {
                if (!currentSnapshot.IsComplete && currentSnapshot.IncompleteReason is not null)
                    DrawBulletText(currentSnapshot.IncompleteReason);
                foreach (var warning in currentSnapshot.Warnings.Distinct())
                    DrawBulletText(warning);
            }
        }

        var visibleResults = currentSnapshot.Results
            .Where(result => ResultsViewState.ShouldInclude(
                result,
                this.configuration.Settings.TargetRank,
                this.configuration.ResultsFilter))
            .Where(result => string.IsNullOrWhiteSpace(this.fcSearch) ||
                             result.FcDisplayName.Contains(this.fcSearch, StringComparison.OrdinalIgnoreCase))
            .OrderBy(result => result.FcDisplayName)
            .ToArray();

        var levelingCount = currentSnapshot.Results.Count(result =>
            !ResultsViewState.IsReady(result, this.configuration.Settings.TargetRank));
        ImGui.Separator();
        ImGui.TextUnformatted($"{visibleResults.Length} shown | {levelingCount} leveling | {currentSnapshot.Results.Count} tracked FCs");

        if (currentSnapshot.Results.Count == 0)
        {
            ImGui.TextUnformatted("No SubmarineTracker data was found.");
            return;
        }

        if (visibleResults.Length == 0)
        {
            ImGui.TextUnformatted("No FCs match the current filter.");
            return;
        }

        foreach (var result in visibleResults)
            DrawFcResult(result);

        this.viewState.ClearExpansionOverride();
    }

    private void DrawResultsToolbar()
    {
        var refreshing = this.refreshTask is { IsCompleted: false };
        if (ImGui.Button(refreshing ? "Cancel refresh" : "Refresh"))
        {
            if (refreshing)
            {
                this.refreshPending = false;
                this.refreshCancellation?.Cancel();
            }
            else
                QueueRefresh();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(180);
        ImGui.InputText("Search FC", ref this.fcSearch, 80);

        ImGui.SameLine();
        DrawFilterButton("Leveling", FcResultFilter.Leveling);
        ImGui.SameLine(0, 2);
        DrawFilterButton("All", FcResultFilter.All);
        ImGui.SameLine(0, 2);
        DrawFilterButton("Ready", FcResultFilter.Ready);

        ImGui.SameLine();
        if (ImGui.Button("Collapse all"))
            this.viewState.CollapseAll();
        ImGui.SameLine();
        if (ImGui.Button("Expand all"))
            this.viewState.ExpandAll();

        ImGui.SameLine();
        ImGui.TextDisabled($"Target {this.configuration.Settings.TargetRank} | {EtaModelLabels[(int)this.configuration.Settings.EtaModel]}");
    }

    private void DrawFilterButton(string label, FcResultFilter filter)
    {
        var selected = this.configuration.ResultsFilter == filter;
        if (selected)
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.48f, 0.70f, 1f));

        if (ImGui.Button(label) && !selected)
        {
            this.configuration.ResultsFilter = filter;
            this.saveConfiguration();
        }

        if (selected)
            ImGui.PopStyleColor();
    }

    private void DrawFcResult(EtaResult result)
    {
        if (this.viewState.ExpansionOverride is not null)
            ImGui.SetNextItemOpen(this.viewState.ExpansionOverride.Value, ImGuiCond.Always);

        var statusText = result.IsComplete
            ? ResultsViewState.IsReady(result, result.TargetRank)
                ? "ready now"
                : $"done {FormatRelative(result.FcCompletionAtUtc, result.GeneratedAtUtc)}"
            : result.IncompleteReason?.Contains("time limit", StringComparison.OrdinalIgnoreCase) == true
                ? "refresh timed out"
                : "incomplete";
        var fcKey = Convert.ToHexString(result.FcId);
        if (!ImGui.CollapsingHeader($"{result.FcDisplayName} - {statusText}###fc-{fcKey}"))
            return;

        if (!result.IsComplete && result.IncompleteReason is not null)
            ImGui.TextColored(new Vector4(1f, 0.65f, 0.25f, 1f), result.IncompleteReason);

        if (!ImGui.BeginTable($"table-{fcKey}", 8, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
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

        foreach (var sub in result.PerSubResults.OrderBy(sub => sub.SubmarineName))
        {
            var subKey = $"{fcKey}:{sub.SubmarineId}";
            var open = this.expandedSubmarines.Contains(subKey);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (ImGui.SmallButton($"{(open ? "v" : ">") }##toggle-{subKey}"))
            {
                if (!this.expandedSubmarines.Add(subKey))
                    this.expandedSubmarines.Remove(subKey);
            }
            ImGui.SameLine();
            ImGui.TextUnformatted(sub.SubmarineName);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{sub.StartingRank} -> {sub.FinalRank}");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(sub.StartingRank >= result.TargetRank ? "now" : FormatRelative(sub.EtaAtUtc, result.GeneratedAtUtc));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(sub.VoyageCount.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(sub.PlannedBuild);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatRoute(sub.NextRoute));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(
                this.configuration.Settings.ShowPost114MrojzReadiness && sub.PostTargetFarmingReady
                    ? "WSCC/MROJZ"
                    : "-");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted((sub.Warnings.Count + (sub.IsComplete ? 0 : 1)).ToString());
        }

        ImGui.EndTable();

        foreach (var sub in result.PerSubResults.OrderBy(sub => sub.SubmarineName))
        {
            var subKey = $"{fcKey}:{sub.SubmarineId}";
            if (!this.expandedSubmarines.Contains(subKey))
                continue;

            ImGui.PushID(subKey);
            ImGui.Separator();
            ImGui.TextUnformatted(sub.SubmarineName);
            DrawSubDetails(sub, this.configuration.Settings.ShowRouteDiagnostics);
            ImGui.PopID();
        }
    }

    private static void DrawSubDetails(PerSubEtaResult sub, bool showDiagnostics)
    {
        if (!sub.IsComplete && sub.IncompleteReason is not null)
            ImGui.TextColored(new Vector4(1f, 0.65f, 0.25f, 1f), sub.IncompleteReason);

        if (sub.Warnings.Count > 0)
        {
            ImGui.TextUnformatted("Warnings");
            foreach (var warning in sub.Warnings.Distinct())
                DrawBulletText(warning);
        }

        if (sub.UnlockMilestones.Count > 0 && ImGui.CollapsingHeader("Unlock milestones"))
        {
            foreach (var milestone in sub.UnlockMilestones)
            {
                DrawBulletText(
                    $"{FormatMilestoneKind(milestone.Kind)}: {milestone.SourcePoint} -> {milestone.UnlockedPoint} " +
                    $"at {milestone.ReturnAtUtc.LocalDateTime:g}");
            }
        }

        var columnCount = showDiagnostics ? 9 : 7;
        if (!ImGui.BeginTable("preview", columnCount, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            return;

        ImGui.TableSetupColumn("#");
        ImGui.TableSetupColumn("Returns");
        ImGui.TableSetupColumn("Build");
        ImGui.TableSetupColumn("Route");
        ImGui.TableSetupColumn("EXP");
        ImGui.TableSetupColumn("Rank");
        ImGui.TableSetupColumn("Repeats");
        if (showDiagnostics)
        {
            ImGui.TableSetupColumn("Per voyage");
            ImGui.TableSetupColumn("EXP/h");
        }
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
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plan.RepeatCount.ToString());
            if (showDiagnostics)
            {
                ImGui.TableNextColumn();
                var perDuration = plan.PerVoyageDuration == TimeSpan.Zero ? plan.Duration : plan.PerVoyageDuration;
                var perExp = plan.ExpPerVoyage == 0 ? plan.ExpGain : plan.ExpPerVoyage;
                ImGui.TextUnformatted($"{FormatDuration(perDuration)} / {perExp:N0} EXP");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(plan.ExpPerHour.ToString("N0"));
            }
        }

        ImGui.EndTable();
    }

    private void DrawSettingsTab()
    {
        var settings = this.draftSettings;
        var changed = false;

        if (ImGui.CollapsingHeader("Simulation", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var etaModel = settings.EtaModel;
            if (DrawEnumCombo("ETA model", EtaModelLabels, ref etaModel))
            {
                settings.EtaModel = etaModel;
                changed = true;
            }

            var target = settings.TargetRank;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("Target rank", ref target))
            {
                settings.TargetRank = Math.Clamp(target, 1, 149);
                changed = true;
            }

            var fleetMode = settings.SimulationMode == SimulationMode.Fleet;
            if (ImGui.Checkbox("Fleet simulation", ref fleetMode))
            {
                settings.SimulationMode = fleetMode ? SimulationMode.Fleet : SimulationMode.OptimisticPerSub;
                changed = true;
            }

            if (settings.EtaModel == EtaModel.PracticalLeveling)
            {
                ImGui.TextUnformatted("EXP mode: Average");
                ImGui.TextUnformatted("Route scoring: maximum total EXP");
                ImGui.TextUnformatted("Unlock policy: main leveling progression");
            }
            else
            {
                var averageExp = settings.ExpMode == ExpMode.Average;
                if (ImGui.Checkbox("Average EXP", ref averageExp))
                {
                    settings.ExpMode = averageExp ? ExpMode.Average : ExpMode.Guaranteed;
                    changed = true;
                }

                var optimize = settings.OptimizeExpPerHour;
                if (ImGui.Checkbox("Optimize EXP/hour", ref optimize))
                {
                    settings.OptimizeExpPerHour = optimize;
                    changed = true;
                }

                var routeGoal = settings.RouteGoal;
                if (DrawEnumCombo("Route goal", RouteGoalLabels, ref routeGoal))
                {
                    settings.RouteGoal = routeGoal;
                    changed = true;
                }
            }

            var delay = settings.CollectionDelayMinutes;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("Collection delay minutes", ref delay))
            {
                settings.CollectionDelayMinutes = Math.Max(0, delay);
                changed = true;
            }
        }

        if (ImGui.CollapsingHeader("Routes", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (settings.EtaModel == EtaModel.PracticalLeveling)
                changed |= DrawPracticalDuration(settings);
            else
            {
                var durationLimit = settings.DurationLimitHours;
                ImGui.SetNextItemWidth(120);
                if (ImGui.InputInt("Duration limit hours (0 = none)", ref durationLimit))
                {
                    settings.DurationLimitHours = Math.Max(0, durationLimit);
                    changed = true;
                }
            }

            var prioritizeSlots = settings.PrioritizeSubSlots;
            if (ImGui.Checkbox("Prioritize missing submarine slots", ref prioritizeSlots))
            {
                settings.PrioritizeSubSlots = prioritizeSlots;
                changed = true;
            }

            var unknownPolicy = settings.UnknownCurrentVoyagePolicy;
            if (DrawEnumCombo("Unknown current voyage", UnknownVoyageLabels, ref unknownPolicy))
            {
                settings.UnknownCurrentVoyagePolicy = unknownPolicy;
                changed = true;
            }
        }

        if (ImGui.CollapsingHeader("Limits", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var timeLimit = settings.CalculationTimeLimitSeconds;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("Calculation time limit seconds", ref timeLimit))
            {
                settings.CalculationTimeLimitSeconds = Math.Clamp(timeLimit, 0, 300);
                changed = true;
            }

            var safetyCap = settings.SimulationSafetyVoyageCapPerSubmarine;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("Voyage safety cap", ref safetyCap))
            {
                settings.SimulationSafetyVoyageCapPerSubmarine = Math.Clamp(safetyCap, 1, 5000);
                changed = true;
            }

            var previewCount = settings.MaxPreviewVoyagesPerSubmarine;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("Preview rows per submarine", ref previewCount))
            {
                settings.MaxPreviewVoyagesPerSubmarine = Math.Clamp(previewCount, 1, 100);
                changed = true;
            }
        }

        if (ImGui.CollapsingHeader("SubmarineTracker", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var dbPath = settings.SubmarineTrackerDatabasePathOverride ?? string.Empty;
            ImGui.SetNextItemWidth(Math.Min(650f, ImGui.GetContentRegionAvail().X));
            if (ImGui.InputText("Database override", ref dbPath, 512))
            {
                settings.SubmarineTrackerDatabasePathOverride = string.IsNullOrWhiteSpace(dbPath) ? null : dbPath.Trim();
                changed = true;
            }
        }

        if (ImGui.CollapsingHeader("Build profile", ImGuiTreeNodeFlags.DefaultOpen))
            changed |= DrawBuildProfile(settings);

        if (ImGui.CollapsingHeader("Display", ImGuiTreeNodeFlags.DefaultOpen))
            DrawDisplaySettings();

        if (changed)
            this.draftDirty = true;

        ImGui.Separator();
        var disableDraftActions = !this.draftDirty;
        if (disableDraftActions)
            ImGui.BeginDisabled();
        if (ImGui.Button("Apply and refresh"))
            ApplyDraftSettings();
        ImGui.SameLine();
        if (ImGui.Button("Revert"))
        {
            this.draftSettings = CloneSettings(this.configuration.Settings);
            this.draftDirty = false;
        }
        if (disableDraftActions)
            ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Reset calculation defaults"))
        {
            this.draftSettings = EtaSettings.CreateDefault();
            this.draftSettings.ShowRouteDiagnostics = this.configuration.Settings.ShowRouteDiagnostics;
            this.draftSettings.ShowPost114MrojzReadiness = this.configuration.Settings.ShowPost114MrojzReadiness;
            this.draftSettings.TimeoutResultBehavior = this.configuration.Settings.TimeoutResultBehavior;
            this.draftDirty = true;
        }

        if (this.draftDirty)
            ImGui.TextColored(new Vector4(1f, 0.75f, 0.25f, 1f), "Calculation settings have unapplied changes.");
    }

    private void DrawDisplaySettings()
    {
        var showDiagnostics = this.configuration.Settings.ShowRouteDiagnostics;
        if (ImGui.Checkbox("Show route diagnostics", ref showDiagnostics))
        {
            this.configuration.Settings.ShowRouteDiagnostics = showDiagnostics;
            this.draftSettings.ShowRouteDiagnostics = showDiagnostics;
            this.saveConfiguration();
        }

        var showReadiness = this.configuration.Settings.ShowPost114MrojzReadiness;
        if (ImGui.Checkbox("Show post-114 MROJZ readiness", ref showReadiness))
        {
            this.configuration.Settings.ShowPost114MrojzReadiness = showReadiness;
            this.draftSettings.ShowPost114MrojzReadiness = showReadiness;
            this.saveConfiguration();
        }

        var timeoutBehavior = this.configuration.Settings.TimeoutResultBehavior;
        if (DrawEnumCombo("Timeout result", TimeoutBehaviorLabels, ref timeoutBehavior))
        {
            this.configuration.Settings.TimeoutResultBehavior = timeoutBehavior;
            this.draftSettings.TimeoutResultBehavior = timeoutBehavior;
            this.saveConfiguration();
        }
    }

    private static bool DrawPracticalDuration(EtaSettings settings)
    {
        var current = Array.IndexOf(PracticalDurations, settings.PracticalMaxVoyageHours);
        if (current < 0)
            current = PracticalDurations.Length - 1;
        var changed = false;

        if (ImGui.BeginCombo("Practical maximum voyage", PracticalDurationLabels[current]))
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
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("Custom maximum hours", ref custom))
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
            ImGui.TableSetupColumn("Min");
            ImGui.TableSetupColumn("Max");
            ImGui.TableSetupColumn("Build");
            ImGui.TableSetupColumn(string.Empty);
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
                if (ImGui.SmallButton("Remove"))
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

        if (ImGui.Button("Add step"))
        {
            settings.BuildProfile.Add(new BuildProfileStep(114, 999, "WSCC"));
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset profile"))
        {
            settings.BuildProfile = EtaSettings.CreateDefault().BuildProfile;
            changed = true;
        }

        return changed;
    }

    private void ApplyDraftSettings()
    {
        this.configuration.Settings = CloneSettings(this.draftSettings);
        this.draftSettings = CloneSettings(this.configuration.Settings);
        this.draftDirty = false;
        this.saveConfiguration();
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (this.refreshTask is { IsCompleted: false })
        {
            this.refreshPending = true;
            this.refreshCancellation?.Cancel();
            return;
        }

        StartRefresh();
    }

    private void StartRefresh()
    {
        if (!IsOpen)
            return;

        this.refreshPending = false;
        this.refreshCancellation?.Dispose();
        this.refreshCancellation = new CancellationTokenSource();
        var cancellationToken = this.refreshCancellation.Token;
        this.refreshStartedAtUtc = DateTimeOffset.UtcNow;
        var settings = CloneSettings(this.configuration.Settings);
        var now = DateTimeOffset.UtcNow;
        Plugin.Log.Information("Starting submarine ETA calculation.");
        this.refreshTask = Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            var result = this.plannerService.Calculate(settings, now, cancellationToken);
            Plugin.Log.Information(
                "Submarine ETA calculation completed in {ElapsedMilliseconds} ms for {FreeCompanyCount} FC(s).",
                stopwatch.ElapsedMilliseconds,
                result.Results.Count);
            return result;
        }, cancellationToken);
    }

    private void CompleteRefreshIfReady()
    {
        var task = this.refreshTask;
        if (task is null || !task.IsCompleted)
            return;

        this.refreshTask = null;
        this.refreshStartedAtUtc = null;
        try
        {
            var result = task.GetAwaiter().GetResult();
            this.lastError = string.Empty;
            if (!result.IsComplete &&
                this.configuration.Settings.TimeoutResultBehavior == TimeoutResultBehavior.KeepLastComplete &&
                this.snapshot is { IsComplete: true })
            {
                this.lastError = result.IncompleteReason ?? "Refresh returned partial results; keeping the last complete table.";
            }
            else
            {
                this.snapshot = result;
            }
        }
        catch (OperationCanceledException)
        {
            if (!this.refreshPending)
                this.lastError = "Refresh cancelled.";
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Submarine ETA calculation failed.");
            this.lastError = ex.Message;
            if (this.snapshot is null)
                this.snapshot = new EtaPlannerSnapshot(DateTimeOffset.UtcNow, [], [], [ex.Message], CalculationStatus.Failed, ex.Message);
        }

        if (this.refreshPending && IsOpen)
            StartRefresh();
    }

    private static bool DrawEnumCombo<TEnum>(string label, IReadOnlyList<string> labels, ref TEnum value)
        where TEnum : struct, Enum
    {
        var current = Convert.ToInt32(value);
        current = Math.Clamp(current, 0, labels.Count - 1);
        var changed = false;
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

    private static EtaSettings CloneSettings(EtaSettings settings) => new()
    {
        TargetRank = settings.TargetRank,
        ExpMode = settings.ExpMode,
        CollectionDelayMinutes = settings.CollectionDelayMinutes,
        SimulationMode = settings.SimulationMode,
        BuildProfile = settings.BuildProfile.Select(step => new BuildProfileStep(step.MinRank, step.MaxRank, step.BuildCode)).ToList(),
        PrioritizeSubSlots = settings.PrioritizeSubSlots,
        RouteGoal = settings.RouteGoal,
        DurationLimitHours = settings.DurationLimitHours,
        EtaModel = settings.EtaModel,
        PracticalMaxVoyageHours = settings.PracticalMaxVoyageHours,
        TimeoutResultBehavior = settings.TimeoutResultBehavior,
        ShowRouteDiagnostics = settings.ShowRouteDiagnostics,
        OptimizeExpPerHour = settings.OptimizeExpPerHour,
        UnknownCurrentVoyagePolicy = settings.UnknownCurrentVoyagePolicy,
        ManualCurrentRouteOverrides = settings.ManualCurrentRouteOverrides.ToDictionary(pair => pair.Key, pair => pair.Value.ToList()),
        ShowPost114MrojzReadiness = settings.ShowPost114MrojzReadiness,
        SubmarineTrackerDatabasePathOverride = settings.SubmarineTrackerDatabasePathOverride,
        MaxPreviewVoyagesPerSubmarine = settings.MaxPreviewVoyagesPerSubmarine,
        SimulationSafetyVoyageCapPerSubmarine = settings.SimulationSafetyVoyageCapPerSubmarine,
        CalculationTimeLimitSeconds = settings.CalculationTimeLimitSeconds,
    };

    private static string NormalizeBuildCode(string value)
    {
        var normalized = new string((value ?? string.Empty).ToUpperInvariant().Where(char.IsLetter).Take(4).ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "SSSS" : normalized;
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

    private static string FormatDuration(TimeSpan duration)
        => duration.TotalDays >= 1
            ? $"{(int)duration.TotalDays}d {duration.Hours}h {duration.Minutes}m"
            : $"{(int)duration.TotalHours}h {duration.Minutes}m";

    private static string FormatRoute(IReadOnlyList<uint> route)
        => route.Count == 0 ? "-" : string.Join("-", route);

    private static string FormatElapsed(TimeSpan elapsed)
        => elapsed.TotalSeconds < 1 ? string.Empty : $"({elapsed:mm\\:ss})";

    private static string FormatMilestoneKind(UnlockMilestoneKind kind) => kind switch
    {
        UnlockMilestoneKind.SectorUnlocked => "Sector unlocked",
        UnlockMilestoneKind.SectorExplored => "Sector explored",
        UnlockMilestoneKind.SubmarineSlotUnlocked => "Submarine slot",
        UnlockMilestoneKind.MapUnlocked => "Map unlocked",
        _ => kind.ToString(),
    };

    private static void DrawBulletText(string text)
    {
        ImGui.Bullet();
        ImGui.SameLine();
        ImGui.TextWrapped(text);
    }

    private enum PlannerTab
    {
        None,
        Results,
        Settings,
    }
}
