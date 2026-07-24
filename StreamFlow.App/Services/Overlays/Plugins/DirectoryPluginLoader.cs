using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace StreamFlow.App.Services.Overlays.Plugins;

/// <summary>
/// Scans the %AppData%/StreamFlow/Plugins directory for .dll assemblies containing classes implementing <see cref="IOverlayTypeDescriptor"/>
/// and registers them with the <see cref="OverlayTypeRegistry"/>.
/// </summary>
public sealed class DirectoryPluginLoader : IPluginLoader
{
    private readonly ILogger<DirectoryPluginLoader> _logger;
    private readonly string _pluginsDirectory;

    public DirectoryPluginLoader(ILogger<DirectoryPluginLoader> logger)
    {
        _logger = logger;
        _pluginsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StreamFlow",
            "Plugins"
        );
    }

    public void LoadPlugins(OverlayTypeRegistry registry)
    {
        try
        {
            if (!Directory.Exists(_pluginsDirectory))
            {
                Directory.CreateDirectory(_pluginsDirectory);
                _logger.LogInformation("Created plugins directory at {Path}", _pluginsDirectory);
                return;
            }

            var dllFiles = Directory.GetFiles(_pluginsDirectory, "*.dll", SearchOption.AllDirectories);
            foreach (var dllPath in dllFiles)
            {
                LoadPluginAssembly(dllPath, registry);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed while loading overlay plugins from directory");
        }
    }

    private void LoadPluginAssembly(string dllPath, OverlayTypeRegistry registry)
    {
        try
        {
            var assemblyName = AssemblyName.GetAssemblyName(dllPath);
            var assembly = Assembly.Load(assemblyName);

            foreach (var type in assembly.GetExportedTypes())
            {
                if (!type.IsAbstract && typeof(IOverlayTypeDescriptor).IsAssignableFrom(type))
                {
                    if (Activator.CreateInstance(type) is IOverlayTypeDescriptor descriptor)
                    {
                        registry.Register(descriptor);
                        _logger.LogInformation("Registered plugin overlay descriptor '{TypeKey}' ({DisplayName}) from {Assembly}", descriptor.TypeKey, descriptor.DisplayName, assemblyName.Name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load overlay plugin assembly at {Path}", dllPath);
        }
    }
}
