using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using SubmarineEtaPlanner.Planner;
using SubmarineEtaPlanner.TrackerData;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

public sealed partial class PlannerWindow : Window
{
    private static readonly TimeSpan TrackerDataCheckInterval = TimeSpan.FromSeconds(5);
    private static readonly string[] EtaModelLabels = ["Recommended leveling", "Custom strategy"];
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
    private readonly ISubmarineCatalog catalog;
    private readonly Func<SubmarineTrackerDependencyState> getSubmarineTrackerState;
    private readonly Action<bool> openSubmarineTrackerInstaller;
    private readonly ResultsViewState viewState = new();
    private readonly HashSet<string> expandedSubmarines = [];
    private readonly ConcurrentQueue<EtaPlannerSnapshot> refreshProgress = new();

    private EtaPlannerSnapshot? snapshot;
    private EtaPlannerSnapshot? refreshBaseSnapshot;
    private Task<EtaPlannerSnapshot>? refreshTask;
    private CancellationTokenSource? refreshCancellation;
    private DateTimeOffset? refreshStartedAtUtc;
    private SubmarineTrackerDataFingerprint? snapshotDataFingerprint;
    private SubmarineTrackerDataFingerprint? refreshDataFingerprint;
    private DateTimeOffset nextTrackerDataCheckAtUtc = DateTimeOffset.MinValue;
    private bool trackerDataChanged;
    private ForecastRefreshMode? pendingRefreshMode;
    private string lastError = string.Empty;
    private string fcSearch = string.Empty;
    private PlannerPage currentPage = PlannerPage.Operations;
    private SettingsSection settingsSection = SettingsSection.Simulation;
    private EtaSettings draftSettings;
    private bool draftDirty;
    private bool resetDefaultsPreviewActive;

    internal PlannerWindow(
        Configuration configuration,
        Action saveConfiguration,
        EtaPlannerService plannerService,
        ISubmarineCatalog catalog,
        Func<SubmarineTrackerDependencyState> getSubmarineTrackerState,
        Action<bool> openSubmarineTrackerInstaller)
        : base("Submarine ETA Planner###SubmarineEtaPlanner", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.configuration = configuration;
        this.saveConfiguration = saveConfiguration;
        this.plannerService = plannerService;
        this.catalog = catalog;
        this.getSubmarineTrackerState = getSubmarineTrackerState;
        this.openSubmarineTrackerInstaller = openSubmarineTrackerInstaller;
        this.draftSettings = CloneSettings(configuration.Settings);
        IsOpen = configuration.WindowOpen;
        Size = new Vector2(1040, 700);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(780, 520),
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
        this.currentPage = PlannerPage.Operations;
        SetOpen(true);
        RefreshIfTrackerDataChanged();
    }

    public void ToggleDashboard()
    {
        if (IsOpen)
        {
            SetOpen(false);
            this.refreshCancellation?.Cancel();
            return;
        }

        OpenResults();
    }

    public void OpenSettings()
    {
        if (!this.draftDirty)
        {
            this.draftSettings = CloneSettings(this.configuration.Settings);
            this.resetDefaultsPreviewActive = false;
        }
        this.currentPage = PlannerPage.Settings;
        SetOpen(true);
    }

    public void OpenAndRefresh()
    {
        this.currentPage = PlannerPage.Operations;
        SetOpen(true);
        QueueRefresh();
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
        ApplyRefreshProgressUpdates();
        CompleteRefreshIfReady();
        using var theme = PlannerUi.PushTheme();

        var available = ImGui.GetContentRegionAvail();
        var compact = available.X < 920f * ImGuiHelpers.GlobalScale;
        var sidebarWidth = (compact ? 52f : 176f) * ImGuiHelpers.GlobalScale;

        ImGui.PushStyleColor(ImGuiCol.ChildBg, PlannerUi.SidebarBackground);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginChild("planner-sidebar", new Vector2(sidebarWidth, -1), true, ImGuiWindowFlags.NoScrollbar))
            DrawSidebar(compact);
        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();

        ImGui.SameLine();
        if (!ImGui.BeginChild("planner-main", new Vector2(-1, -1), false, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.EndChild();
            return;
        }

        var refreshing = this.refreshTask is { IsCompleted: false };
        if (PlannerUi.DrawHeader(
                GetPageTitle(),
                GetPageSubtitle(),
                this.configuration.Settings.TargetRank,
                EtaModelLabels[(int)this.configuration.Settings.EtaModel],
                this.currentPage is PlannerPage.Operations or PlannerPage.Leveling or PlannerPage.Income,
                refreshing,
                this.currentPage is PlannerPage.Operations or PlannerPage.Leveling or PlannerPage.Income,
                this.configuration.FreeCompanyPreferences.Values.Count(value => value.TargetRankOverride is not null || value.StrategyOverride is not null)))
        {
            if (refreshing)
            {
                this.pendingRefreshMode = null;
                this.refreshCancellation?.Cancel();
            }
            else
            {
                QueueRefresh();
            }
        }

        ImGui.Spacing();
        if (this.currentPage == PlannerPage.Settings)
        {
            var displayPage = this.settingsSection == SettingsSection.Display;
            var showActionBar = !displayPage || this.resetDefaultsPreviewActive;
            var actionBarHeight = showActionBar ? 64f * ImGuiHelpers.GlobalScale : 0f;
            if (ImGui.BeginChild("settings-scroll", new Vector2(-1, -actionBarHeight), false))
                DrawSettingsPage();
            ImGui.EndChild();

            if (showActionBar)
                DrawSettingsActionBar();
        }
        else
        {
            if (ImGui.BeginChild("fleet-screen-scroll", new Vector2(-1, -1), false))
            {
                switch (this.currentPage)
                {
                    case PlannerPage.Operations: DrawOperationsPage(); break;
                    case PlannerPage.Leveling: DrawLevelingPage(); break;
                    case PlannerPage.Income: DrawIncomePage(); break;
                    case PlannerPage.FcSetup: DrawFcSetupPage(); break;
                }
            }
            ImGui.EndChild();
        }

        ImGui.EndChild();
    }

    private void DrawSidebar(bool compact)
    {
        PlannerUi.DrawBrandMark(compact);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawNavigationItem(PlannerPage.Operations, FontAwesomeIcon.Tasks, "Operations", compact);
        DrawNavigationItem(PlannerPage.Leveling, FontAwesomeIcon.ChartLine, "Leveling", compact);
        DrawNavigationItem(PlannerPage.Income, FontAwesomeIcon.Coins, "Income", compact);
        DrawNavigationItem(PlannerPage.FcSetup, FontAwesomeIcon.Building, "FC Setup", compact);
        ImGui.Spacing();
        DrawNavigationItem(PlannerPage.Settings, FontAwesomeIcon.Cogs, "Settings", compact);

        if (this.draftDirty)
        {
            ImGui.Spacing();
            if (compact)
            {
                PlannerUi.DrawStatusPill("!", PlannerUi.Amber);
                PlannerUi.Tooltip("Calculation settings have unapplied changes.");
            }
            else
            {
                PlannerUi.DrawStatusPill("Unapplied changes", PlannerUi.Amber);
            }
        }
    }

    private void DrawNavigationItem(PlannerPage page, FontAwesomeIcon icon, string label, bool compact)
    {
        if (PlannerUi.NavigationButton($"nav-{page}", icon, label, compact, this.currentPage == page))
            this.currentPage = page;
    }

    private void SetOpen(bool isOpen)
    {
        IsOpen = isOpen;
        this.configuration.WindowOpen = isOpen;
        this.saveConfiguration();
    }

    private string GetPageTitle() => this.currentPage switch
    {
        PlannerPage.Operations => "Fleet operations",
        PlannerPage.Leveling => "Leveling fleets",
        PlannerPage.Income => "Recorded income",
        PlannerPage.FcSetup => "Free company setup",
        PlannerPage.Settings => "Settings",
        _ => "Submarine ETA Planner",
    };

    private string GetPageSubtitle() => this.currentPage switch
    {
        PlannerPage.Operations => "Actions first, then every known return across the fleet.",
        PlannerPage.Leveling => "Every leveling FC and submarine in one progression view.",
        PlannerPage.Income => "Gross NPC salvage value from recorded SubmarineTracker returns.",
        PlannerPage.FcSetup => "Favorites and per-FC target and strategy overrides.",
        PlannerPage.Settings => "Global simulation, route, data, build, and display preferences.",
        _ => string.Empty,
    };

    private void ApplyDraftSettings()
    {
        this.configuration.Settings = CloneSettings(this.draftSettings);
        this.draftSettings = CloneSettings(this.configuration.Settings);
        this.draftDirty = false;
        this.resetDefaultsPreviewActive = false;
        this.saveConfiguration();
        QueueRefresh();
    }

    private void QueueRefresh(ForecastRefreshMode mode = ForecastRefreshMode.Full)
    {
        if (this.snapshot is null)
            mode = ForecastRefreshMode.Full;

        if (this.refreshTask is { IsCompleted: false })
        {
            this.pendingRefreshMode = StrongerRefreshMode(this.pendingRefreshMode, mode);
            this.refreshCancellation?.Cancel();
            return;
        }

        StartRefresh(mode);
    }

    private void StartRefresh(ForecastRefreshMode mode = ForecastRefreshMode.Full)
    {
        if (!IsOpen)
            return;

        var dependency = this.getSubmarineTrackerState();
        if (!dependency.IsAvailable)
        {
            Plugin.Log.Warning(
                dependency.IsInstalled
                    ? "SubmarineTracker is installed but not loaded; forecast refresh skipped."
                    : "SubmarineTracker is not installed; forecast refresh skipped.");
            return;
        }

        this.pendingRefreshMode = null;
        this.refreshCancellation?.Dispose();
        this.refreshCancellation = new CancellationTokenSource();
        var cancellationToken = this.refreshCancellation.Token;
        this.refreshStartedAtUtc = DateTimeOffset.UtcNow;
        this.refreshBaseSnapshot = this.snapshot;
        var previousSnapshot = this.refreshBaseSnapshot;
        while (this.refreshProgress.TryDequeue(out _))
        {
        }
        var settings = CloneSettings(this.configuration.Settings);
        var calculationRequest = new PlannerCalculationRequest(settings, this.configuration.GetSimulationOverrides());
        this.refreshDataFingerprint = this.plannerService.GetDataFingerprint(settings);
        var now = DateTimeOffset.UtcNow;
        Plugin.Log.Information("Starting submarine ETA calculation.");
        this.refreshTask = Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            var result = this.plannerService.Calculate(
                calculationRequest,
                now,
                cancellationToken,
                progress => this.refreshProgress.Enqueue(progress),
                previousSnapshot,
                mode);
            Plugin.Log.Information(
                "Submarine ETA calculation completed in {ElapsedMilliseconds} ms for {FreeCompanyCount} FC(s).",
                stopwatch.ElapsedMilliseconds,
                result.Results.Count);
            return result;
        }, cancellationToken);
    }

    private void CompleteRefreshIfReady()
    {
        ApplyRefreshProgressUpdates();
        var task = this.refreshTask;
        if (task is null || !task.IsCompleted)
            return;

        this.refreshTask = null;
        this.refreshStartedAtUtc = null;
        try
        {
            var result = task.GetAwaiter().GetResult();
            this.lastError = string.Empty;
            this.snapshot = MergeProgressSnapshot(result);
            this.snapshotDataFingerprint = this.refreshDataFingerprint;
            this.trackerDataChanged = false;
            this.nextTrackerDataCheckAtUtc = DateTimeOffset.MinValue;
        }
        catch (OperationCanceledException)
        {
            if (this.refreshBaseSnapshot is not null)
                this.snapshot = this.refreshBaseSnapshot;
            else if (this.snapshot is not null)
                this.snapshot = MarkSnapshotCancelled(this.snapshot);
            if (this.pendingRefreshMode is null)
                this.lastError = "Refresh cancelled. Existing results were kept.";
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Submarine ETA calculation failed.");
            this.lastError = ex.Message;
            if (this.snapshot is null)
                this.snapshot = new EtaPlannerSnapshot(DateTimeOffset.UtcNow, [], [], [ex.Message], CalculationStatus.Failed, ex.Message);
        }

        this.refreshBaseSnapshot = null;

        if (this.pendingRefreshMode is { } pendingMode && IsOpen)
            StartRefresh(pendingMode);
    }

    private void ApplyRefreshProgressUpdates()
    {
        EtaPlannerSnapshot? latest = null;
        while (this.refreshProgress.TryDequeue(out var update))
            latest = update;

        if (latest is not null)
            this.snapshot = MergeProgressSnapshot(latest);
    }

    private EtaPlannerSnapshot MergeProgressSnapshot(EtaPlannerSnapshot progress)
    {
        var results = progress.Results.ToDictionary(result => Convert.ToHexString(result.FcId));
        if (this.refreshBaseSnapshot is not null)
        {
            var currentFcIds = progress.FreeCompanies.Select(fc => fc.FcIdKey).ToHashSet();
            foreach (var previous in this.refreshBaseSnapshot.Results)
            {
                var key = Convert.ToHexString(previous.FcId);
                if (!currentFcIds.Contains(key))
                    continue;

                if (!results.TryGetValue(key, out var replacement) ||
                    (this.configuration.Settings.TimeoutResultBehavior == TimeoutResultBehavior.KeepLastComplete &&
                     previous.IsComplete &&
                     !replacement.IsComplete))
                {
                    results[key] = previous;
                }
            }
        }

        return progress with
        {
            Results = progress.FreeCompanies
                .Where(fc => results.ContainsKey(fc.FcIdKey))
                .Select(fc => results[fc.FcIdKey])
                .ToArray(),
        };
    }

    private static EtaPlannerSnapshot MarkSnapshotCancelled(EtaPlannerSnapshot current)
        => current with
        {
            Status = CalculationStatus.Partial,
            IncompleteReason = "Refresh cancelled.",
            IsRunning = false,
            FcProgress = current.FcProgress.Select(progress =>
                progress.Status is FcCalculationStatus.Queued or FcCalculationStatus.Calculating
                    ? progress with
                    {
                        Status = FcCalculationStatus.Cancelled,
                        CompletedAtUtc = DateTimeOffset.UtcNow,
                        Message = "Refresh cancelled.",
                    }
                    : progress).ToArray(),
        };

    private void RefreshIfTrackerDataChanged()
    {
        if (this.snapshot is null)
        {
            QueueRefresh(ForecastRefreshMode.Full);
            return;
        }

        CheckForTrackerDataChanges(force: true);
        if (this.trackerDataChanged)
            QueueRefresh(ForecastRefreshMode.Incremental);
    }

    private static ForecastRefreshMode StrongerRefreshMode(
        ForecastRefreshMode? pending,
        ForecastRefreshMode requested)
        => pending is null || (int)requested > (int)pending.Value ? requested : pending.Value;

    private void CheckForTrackerDataChanges(bool force = false)
    {
        if (this.snapshot is null)
        {
            this.trackerDataChanged = false;
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!force && now < this.nextTrackerDataCheckAtUtc)
            return;

        this.nextTrackerDataCheckAtUtc = now + TrackerDataCheckInterval;
        try
        {
            var current = this.plannerService.GetDataFingerprint(this.configuration.Settings);
            this.trackerDataChanged = this.snapshotDataFingerprint is null ||
                                      current != this.snapshotDataFingerprint ||
                                      HasCrossedVoyageReturnBoundary(this.snapshot, now);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not check whether SubmarineTracker data changed.");
        }
    }

    private bool HasCrossedVoyageReturnBoundary(
        EtaPlannerSnapshot snapshot,
        DateTimeOffset now)
    {
        var awaitingFcIds = snapshot.FcProgress
            .Where(progress => progress.Status == FcCalculationStatus.AwaitingTrackerUpdate)
            .Select(progress => progress.FcIdKey)
            .ToHashSet();
        return snapshot.FreeCompanies.Any(fc =>
            !awaitingFcIds.Contains(fc.FcIdKey) &&
            fc.Submarines.Any(submarine =>
                submarine.Rank < EffectiveEtaSettingsResolver.Resolve(
                    this.configuration.Settings,
                    this.configuration.GetSimulationOverrides().GetValueOrDefault(fc.FcIdKey),
                    this.catalog.MaximumRank).TargetRank &&
                submarine.CurrentRoute.Count > 0 &&
                submarine.ReturnAtUtc > snapshot.GeneratedAtUtc &&
                submarine.ReturnAtUtc <= now));
    }

    private static EtaSettings CloneSettings(EtaSettings settings) => settings.DeepClone();

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

    private string FormatRoute(IReadOnlyList<uint> route)
        => route.Count == 0 ? "-" : string.Join(" → ", route.Select(this.catalog.PointName));

    private string FormatCompactRoute(IReadOnlyList<uint> route)
        => RouteDisplayFormatter.FormatCompactRoute(route, this.catalog.PointName);

    private string FormatPoint(uint point) => this.catalog.PointName(point);

    private void DrawRoute(IReadOnlyList<uint> route)
    {
        var routeText = FormatRoute(route);
        ImGui.TextColored(PlannerUi.Cyan, routeText);
        if (route.Count == 0 || !ImGui.IsItemHovered())
            return;

        ImGui.BeginTooltip();
        ImGui.TextColored(PlannerUi.Teal, "Full route");
        ImGui.Separator();
        for (var index = 0; index < route.Count; index++)
            ImGui.TextUnformatted($"{index + 1}. {FormatPoint(route[index])}");
        ImGui.EndTooltip();
    }

    private void DrawCompactRoute(IReadOnlyList<uint> route, Vector4? color = null)
    {
        var routeColor = route.Count == 0 ? PlannerUi.Muted : color ?? PlannerUi.Cyan;
        ImGui.TextColored(routeColor, FormatCompactRoute(route));
        if (route.Count == 0 || !ImGui.IsItemHovered())
            return;

        ImGui.BeginTooltip();
        ImGui.TextColored(PlannerUi.Teal, "Full route");
        ImGui.Separator();
        for (var index = 0; index < route.Count; index++)
            ImGui.TextUnformatted($"{index + 1}. {FormatPoint(route[index])}");
        ImGui.EndTooltip();
    }

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

    private enum PlannerPage
    {
        Operations,
        Leveling,
        Income,
        FcSetup,
        Settings,
    }

    private enum SettingsSection
    {
        Simulation,
        Routes,
        Limits,
        DataSource,
        BuildProfile,
        Display,
    }
}
