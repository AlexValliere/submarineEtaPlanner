using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using SubmarineEtaPlanner.Planner;
using System.Diagnostics;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

public sealed partial class PlannerWindow : Window
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
    private readonly ISubmarineCatalog catalog;
    private readonly Func<SubmarineTrackerDependencyState> getSubmarineTrackerState;
    private readonly Action<bool> openSubmarineTrackerInstaller;
    private readonly ResultsViewState viewState = new();
    private readonly HashSet<string> expandedSubmarines = [];

    private EtaPlannerSnapshot? snapshot;
    private Task<EtaPlannerSnapshot>? refreshTask;
    private CancellationTokenSource? refreshCancellation;
    private DateTimeOffset? refreshStartedAtUtc;
    private bool refreshPending;
    private string lastError = string.Empty;
    private string fcSearch = string.Empty;
    private PlannerPage currentPage = PlannerPage.Dashboard;
    private EtaSettings draftSettings;
    private bool draftDirty;

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
        this.currentPage = PlannerPage.Dashboard;
        SetOpen(true);
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
            this.draftSettings = CloneSettings(this.configuration.Settings);
        this.currentPage = PlannerPage.Simulation;
        SetOpen(true);
    }

    public void OpenAndRefresh()
    {
        OpenResults();
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
                this.currentPage == PlannerPage.Dashboard,
                refreshing))
        {
            if (refreshing)
            {
                this.refreshPending = false;
                this.refreshCancellation?.Cancel();
            }
            else
            {
                QueueRefresh();
            }
        }

        ImGui.Spacing();
        if (this.currentPage == PlannerPage.Dashboard)
        {
            if (ImGui.BeginChild("dashboard-scroll", new Vector2(-1, -1), false))
                DrawDashboardPage();
            ImGui.EndChild();
        }
        else
        {
            var displayPage = this.currentPage == PlannerPage.Display;
            var actionBarHeight = displayPage ? 0f : 64f * ImGuiHelpers.GlobalScale;
            if (ImGui.BeginChild("settings-scroll", new Vector2(-1, -actionBarHeight), false))
                DrawSettingsPage();
            ImGui.EndChild();

            if (!displayPage)
                DrawSettingsActionBar();
        }

        ImGui.EndChild();
    }

    private void DrawSidebar(bool compact)
    {
        PlannerUi.DrawBrandMark(compact);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawNavigationItem(PlannerPage.Dashboard, FontAwesomeIcon.ChartLine, "Dashboard", compact);
        ImGui.Spacing();
        if (!compact)
            PlannerUi.SectionLabel("SETTINGS");

        DrawNavigationItem(PlannerPage.Simulation, FontAwesomeIcon.Cogs, "Simulation", compact);
        DrawNavigationItem(PlannerPage.Routes, FontAwesomeIcon.Map, "Routes", compact);
        DrawNavigationItem(PlannerPage.Limits, FontAwesomeIcon.TachometerAlt, "Limits", compact);
        DrawNavigationItem(PlannerPage.DataSource, FontAwesomeIcon.Database, "Data source", compact);
        DrawNavigationItem(PlannerPage.BuildProfile, FontAwesomeIcon.Wrench, "Build profile", compact);
        DrawNavigationItem(PlannerPage.Display, FontAwesomeIcon.Eye, "Display", compact);

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
        PlannerPage.Dashboard => "Submarine ETA Planner",
        PlannerPage.Simulation => "Simulation",
        PlannerPage.Routes => "Route strategy",
        PlannerPage.Limits => "Calculation limits",
        PlannerPage.DataSource => "SubmarineTracker data",
        PlannerPage.BuildProfile => "Build profile",
        PlannerPage.Display => "Display preferences",
        _ => "Submarine ETA Planner",
    };

    private string GetPageSubtitle() => this.currentPage switch
    {
        PlannerPage.Dashboard => "Forecast every tracked fleet from one calm command deck.",
        PlannerPage.Simulation => "Choose the model, target rank, and fleet assumptions.",
        PlannerPage.Routes => "Control voyage duration and unlock priorities.",
        PlannerPage.Limits => "Keep long-running calculations within safe boundaries.",
        PlannerPage.DataSource => "Use the default tracker database or provide an override.",
        PlannerPage.BuildProfile => "Assign the build used throughout each rank range.",
        PlannerPage.Display => "Tune diagnostics and result presentation.",
        _ => string.Empty,
    };

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

        var dependency = this.getSubmarineTrackerState();
        if (!dependency.IsAvailable)
        {
            Plugin.Log.Warning(
                dependency.IsInstalled
                    ? "SubmarineTracker is installed but not loaded; forecast refresh skipped."
                    : "SubmarineTracker is not installed; forecast refresh skipped.");
            return;
        }

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
                this.lastError = "Refresh cancelled. Existing results were kept.";
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

    private string FormatRoute(IReadOnlyList<uint> route)
        => route.Count == 0 ? "-" : string.Join(" → ", route.Select(this.catalog.PointName));

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
        Dashboard,
        Simulation,
        Routes,
        Limits,
        DataSource,
        BuildProfile,
        Display,
    }
}
