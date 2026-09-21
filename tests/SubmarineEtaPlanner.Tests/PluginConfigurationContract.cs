// The runtime Configuration is linked into the core-only test project. This minimal
// host contract allows its real migration/serialization behavior to be tested without Dalamud.
namespace Dalamud.Configuration;

public interface IPluginConfiguration
{
    int Version { get; set; }
}
