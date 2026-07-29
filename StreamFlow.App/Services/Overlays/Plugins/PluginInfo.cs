using CommunityToolkit.Mvvm.ComponentModel;
using StreamFlow.Plugin.SDK;

namespace StreamFlow.App.Services.Overlays.Plugins;

public partial class PluginInfo : ObservableObject
{
    public required string PluginId { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Author { get; init; }
    public required string Description { get; init; }
    public required string AssemblyPath { get; init; }
    public bool HasConfiguration { get; init; }
    public IPluginDescriptor? Descriptor { get; init; }
    public PluginManifest? Manifest { get; init; }

    [ObservableProperty]
    private bool _isEnabled = true;
}
