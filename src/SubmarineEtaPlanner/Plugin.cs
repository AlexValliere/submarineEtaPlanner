using Dalamud.IoC;
using Dalamud.Game.Command;
using Dalamud.Interface;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SubmarineEtaPlanner.Fuel;
using SubmarineEtaPlanner.Planner;
using SubmarineEtaPlanner.SubmarineTrackerRuntime;
using SubmarineEtaPlanner.SubmarineTrackerCompat;
using SubmarineEtaPlanner.TrackerData;
using SubmarineEtaPlanner.Ui;

namespace SubmarineEtaPlanner;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/seta";

    public string Name => "Submarine ETA Planner";

    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    [PluginService]
    internal static IDataManager Data { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IChatGui ChatGui { get; private set; } = null!;

    [PluginService]
    internal static IClientState ClientState { get; private set; } = null!;

    [PluginService]
    internal static IPlayerState PlayerState { get; private set; } = null!;

    [PluginService]
    internal static IFramework Framework { get; private set; } = null!;

    private readonly PlannerWindow plannerWindow;
    private readonly FuelObservationCoordinator fuelObservationCoordinator;
    private readonly List<string> fuelObservationWarnings = [];
    private readonly Dalamud.Interface.Windowing.WindowSystem windowSystem = new("SubmarineEtaPlanner");
    private int loggedFuelObservationWarningCount;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (Configuration.Migrate())
            SaveConfiguration();

        var fuelObservationPath = Path.Combine(
            PluginInterface.GetPluginConfigDirectory(),
            "workshop-fuel-observations.json");
        var fuelReader = new CurrentCharacterFuelReader(
            new DalamudGameFuelInventoryReader(ClientState, PlayerState, Framework),
            (exception, message) => Log.Error(exception, message));
        this.fuelObservationCoordinator = new FuelObservationCoordinator(
            fuelReader,
            new JsonFuelObservationStore(fuelObservationPath),
            this.fuelObservationWarnings);
        LogFuelObservationWarnings();

        ISubmarineCatalog catalog = new DalamudSubmarineCatalog(Data, PluginInterface.AssemblyLocation.DirectoryName!, Log);
        var stateReader = new SubmarineTrackerStateReader(new DalamudSalvageValueCatalog(Data));
        var buildResolver = new BuildResolver(catalog);
        var unlockGraph = new RouteUnlockGraph(catalog);
        var routeSelector = new RouteSelector(catalog, unlockGraph);
        var simulator = new EtaSimulator(buildResolver, unlockGraph, routeSelector, catalog);
        var service = new EtaPlannerService(
            stateReader,
            simulator,
            catalog as IRouteSearchDiagnostics,
            catalog as IPlannerDataDiagnostics,
            catalog.MaximumRank);

        this.plannerWindow = new PlannerWindow(
            Configuration,
            SaveConfiguration,
            service,
            catalog,
            () => this.fuelObservationCoordinator.Observations,
            characterId => this.fuelObservationCoordinator.ForgetObservation(characterId),
            GetSubmarineTrackerState,
            OpenSubmarineTrackerInstaller);
        this.windowSystem.AddWindow(this.plannerWindow);

        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += OpenMainUi;
        Framework.Update += OnFrameworkUpdate;
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle Submarine ETA Planner. Subcommands: settings, refresh, help.",
            ShowInHelp = true,
        });
    }

    internal Configuration Configuration { get; }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;
        this.plannerWindow.CancelRefresh();
        this.windowSystem.RemoveAllWindows();
        this.fuelObservationCoordinator.Dispose();
        LogFuelObservationWarnings();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            this.fuelObservationCoordinator.Tick(DateTimeOffset.UtcNow);
            LogFuelObservationWarnings();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update locally observed workshop fuel stock.");
        }
    }

    private void LogFuelObservationWarnings()
    {
        while (this.loggedFuelObservationWarningCount < this.fuelObservationWarnings.Count)
        {
            Log.Warning("{Warning}", this.fuelObservationWarnings[this.loggedFuelObservationWarningCount]);
            this.loggedFuelObservationWarningCount++;
        }
    }

    private void Draw()
    {
        try
        {
            this.windowSystem.Draw();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while drawing Submarine ETA Planner UI.");
        }
    }

    private void OpenConfigUi()
    {
        this.plannerWindow.OpenSettings();
        SaveConfiguration();
    }

    private void OpenMainUi()
    {
        this.plannerWindow.OpenResults();
        SaveConfiguration();
    }

    private void OnCommand(string command, string arguments)
    {
        var subcommand = arguments.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
        switch (subcommand)
        {
            case null:
                this.plannerWindow.ToggleDashboard();
                break;
            case "settings":
                this.plannerWindow.OpenSettings();
                break;
            case "refresh":
                this.plannerWindow.OpenAndRefresh();
                break;
            case "help":
                PrintCommandHelp();
                break;
            default:
                ChatGui.PrintError($"Unknown Submarine ETA Planner command: {subcommand}");
                PrintCommandHelp();
                break;
        }
    }

    private static void PrintCommandHelp()
    {
        ChatGui.Print("Submarine ETA Planner commands:");
        ChatGui.Print("/seta — Toggle the dashboard");
        ChatGui.Print("/seta settings — Open simulation settings");
        ChatGui.Print("/seta refresh — Open the dashboard and refresh the forecast");
        ChatGui.Print("/seta help — Show this help");
    }

    private static SubmarineTrackerDependencyState GetSubmarineTrackerState()
    {
        var tracker = PluginInterface.InstalledPlugins.FirstOrDefault(plugin =>
            plugin.InternalName.Equals("SubmarineTracker", StringComparison.OrdinalIgnoreCase));
        return new SubmarineTrackerDependencyState(tracker is not null, tracker?.IsLoaded == true);
    }

    private static void OpenSubmarineTrackerInstaller(bool installed)
        => PluginInterface.OpenPluginInstallerTo(
            installed ? PluginInstallerOpenKind.InstalledPlugins : PluginInstallerOpenKind.AllPlugins,
            "Submarine Tracker");

    private void SaveConfiguration() => PluginInterface.SavePluginConfig(Configuration);
}
