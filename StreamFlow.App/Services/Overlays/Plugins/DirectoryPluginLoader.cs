using StreamFlow.Plugin.SDK;

namespace StreamFlow.App.Services.Overlays.Plugins;

/// <summary>
/// Delegates plugin discovery and loading to <see cref="PluginManagerService"/>.
/// </summary>
public sealed class DirectoryPluginLoader : IPluginLoader
{
    private readonly PluginManagerService _pluginManager;

    public DirectoryPluginLoader(PluginManagerService pluginManager)
    {
        _pluginManager = pluginManager;
    }

    public void LoadPlugins(OverlayTypeRegistry registry)
    {
        _pluginManager.LoadPlugins();
    }
}
