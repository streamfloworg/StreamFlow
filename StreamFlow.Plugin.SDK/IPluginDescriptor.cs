using System.Windows;

namespace StreamFlow.Plugin.SDK;

/// <summary>
/// Metadata and configuration entrypoint for StreamFlow plugins.
/// </summary>
public interface IPluginDescriptor
{
    string PluginId { get; }
    string Name { get; }
    string Version { get; }
    string Author { get; }
    string Description { get; }
    bool HasConfiguration { get; }

    /// <summary>Returns a WPF FrameworkElement for configuring plugin settings in the Plugin Manager UI.</summary>
    FrameworkElement? CreateConfigurationControl();
}
