namespace StreamFlow.App.Services.Overlays.Plugins;

/// <summary>
/// Service interface for discovering and loading external plugin overlay descriptors at runtime.
/// </summary>
public interface IPluginLoader
{
    void LoadPlugins(OverlayTypeRegistry registry);
}
