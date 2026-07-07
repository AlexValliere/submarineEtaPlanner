using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SubmarineEtaPlanner.Planner;
using SubmarineEtaPlanner.SubmarineTrackerRuntime;
using SubmarineEtaPlanner.SubmarineTrackerCompat;
using SubmarineEtaPlanner.TrackerData;
using SubmarineEtaPlanner.Ui;

namespace SubmarineEtaPlanner;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Submarine ETA Planner";

    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    [PluginService]
    internal static IDataManager Data { get; private set; } = null!;

    private readonly PlannerWindow plannerWindow;
    private readonly Dalamud.Interface.Windowing.WindowSystem windowSystem = new("SubmarineEtaPlanner");

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        ISubmarineCatalog catalog = new DalamudSubmarineCatalog(Data, PluginInterface.AssemblyLocation.DirectoryName!, Log);
        var stateReader = new SubmarineTrackerStateReader();
        var buildResolver = new BuildResolver(catalog);
        var unlockGraph = new RouteUnlockGraph(catalog);
        var routeSelector = new RouteSelector(catalog, unlockGraph);
        var simulator = new EtaSimulator(buildResolver, unlockGraph, routeSelector, catalog);
        var service = new EtaPlannerService(stateReader, simulator);

        this.plannerWindow = new PlannerWindow(Configuration, SaveConfiguration, service);
        this.windowSystem.AddWindow(this.plannerWindow);

        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += OpenMainUi;
    }

    internal Configuration Configuration { get; }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;
        this.windowSystem.RemoveAllWindows();
    }

    private void Draw() => this.windowSystem.Draw();

    private void OpenConfigUi() => OpenMainUi();

    private void OpenMainUi()
    {
        Configuration.WindowOpen = true;
        this.plannerWindow.IsOpen = true;
    }

    private void SaveConfiguration() => PluginInterface.SavePluginConfig(Configuration);
}
