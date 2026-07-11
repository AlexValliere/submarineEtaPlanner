using Dalamud.Configuration;
using SubmarineEtaPlanner.Planner;

namespace SubmarineEtaPlanner;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = EtaSettingsMigration.CurrentVersion;

    public EtaSettings Settings { get; set; } = null!;

    public bool WindowOpen { get; set; } = true;

    public FcResultFilter ResultsFilter { get; set; } = FcResultFilter.Leveling;

    public bool Migrate()
    {
        Settings ??= EtaSettings.CreateDefault();
        var version = Version;
        var changed = EtaSettingsMigration.Migrate(Settings, ref version);
        Version = version;
        return changed;
    }
}
