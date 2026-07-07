using Dalamud.Configuration;
using SubmarineEtaPlanner.Planner;

namespace SubmarineEtaPlanner;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public EtaSettings Settings { get; set; } = EtaSettings.CreateDefault();

    public bool WindowOpen { get; set; } = true;
}
