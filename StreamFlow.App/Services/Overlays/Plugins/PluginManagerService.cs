using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StreamFlow.Plugin.SDK;

namespace StreamFlow.App.Services.Overlays.Plugins;

public sealed class PluginManagerService
{
    private readonly ILogger<PluginManagerService> _logger;
    private readonly OverlayTypeRegistry _registry;
    private readonly string _pluginsDirectory;
    private readonly string _manifestPath;

    public ObservableCollection<PluginInfo> InstalledPlugins { get; } = [];

    static PluginManagerService()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            var name = new AssemblyName(args.Name).Name;
            if (string.IsNullOrEmpty(name)) return null;

            var existing = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing;

            var baseFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{name}.dll");
            if (File.Exists(baseFile))
            {
                try { return Assembly.Load(File.ReadAllBytes(baseFile)); } catch { }
            }
            return null;
        };
    }

    public PluginManagerService(ILogger<PluginManagerService> logger, OverlayTypeRegistry registry)
        : this(logger, registry, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StreamFlow", "Plugins"))
    {
    }

    public PluginManagerService(ILogger<PluginManagerService> logger, OverlayTypeRegistry registry, string pluginsDirectory)
    {
        _logger = logger;
        _registry = registry;
        _pluginsDirectory = pluginsDirectory;
        _manifestPath = Path.Combine(_pluginsDirectory, "plugins_state.json");
    }

    public void LoadPlugins()
    {
        InstalledPlugins.Clear();
        if (!Directory.Exists(_pluginsDirectory))
        {
            Directory.CreateDirectory(_pluginsDirectory);
        }

        // Unpack any loose .sfplugin or .zip packages in the plugins directory root
        var archiveFiles = Directory.GetFiles(_pluginsDirectory, "*.sfplugin")
            .Concat(Directory.GetFiles(_pluginsDirectory, "*.zip"));
        foreach (var archivePath in archiveFiles)
        {
            ExtractArchive(archivePath);
        }

        var disabledIds = LoadDisabledPluginIds();
        var dllFiles = Directory.GetFiles(_pluginsDirectory, "*.dll", SearchOption.AllDirectories);

        foreach (var dllPath in dllFiles)
        {
            try
            {
                var dir = Path.GetDirectoryName(dllPath);
                PluginManifest? manifest = null;
                if (dir is not null)
                {
                    var manifestFile = Path.Combine(dir, "plugin.json");
                    if (File.Exists(manifestFile))
                    {
                        try
                        {
                            var json = File.ReadAllText(manifestFile);
                            manifest = JsonSerializer.Deserialize<PluginManifest>(json);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed reading plugin.json at {Path}", manifestFile);
                        }
                    }
                }

                byte[] rawBytes = File.ReadAllBytes(dllPath);
                var pdbPath = Path.ChangeExtension(dllPath, ".pdb");
                Assembly asm = File.Exists(pdbPath)
                    ? Assembly.Load(rawBytes, File.ReadAllBytes(pdbPath))
                    : Assembly.Load(rawBytes);

                Type[] types;
                try
                {
                    types = asm.GetExportedTypes();
                }
                catch (ReflectionTypeLoadException rtle)
                {
                    types = rtle.Types.Where(t => t is not null).ToArray()!;
                }

                foreach (var type in types)
                {
                    if (type is null || type.IsAbstract) continue;

                    bool isPlugin = typeof(IPluginDescriptor).IsAssignableFrom(type) ||
                                    type.GetInterfaces().Any(i => i.FullName == typeof(IPluginDescriptor).FullName);

                    if (isPlugin)
                    {
                        if (Activator.CreateInstance(type) is IPluginDescriptor descriptor)
                        {
                            var pluginInfo = new PluginInfo
                            {
                                PluginId = descriptor.PluginId,
                                Name = manifest?.Name ?? descriptor.Name,
                                Version = manifest?.Version ?? descriptor.Version,
                                Author = manifest?.Author ?? descriptor.Author,
                                Description = manifest?.Description ?? descriptor.Description,
                                AssemblyPath = dllPath,
                                HasConfiguration = descriptor.HasConfiguration,
                                Descriptor = descriptor,
                                Manifest = manifest,
                                IsEnabled = !disabledIds.Contains(descriptor.PluginId)
                            };

                            pluginInfo.PropertyChanged += (_, e) =>
                            {
                                if (e.PropertyName == nameof(PluginInfo.IsEnabled))
                                {
                                    if (pluginInfo.Descriptor is IOverlayTypeDescriptor overlayDesc)
                                    {
                                        if (pluginInfo.IsEnabled)
                                            _registry.Register(overlayDesc);
                                        else
                                            _registry.Unregister(overlayDesc);
                                    }
                                    SavePluginStates();
                                }
                            };

                            InstalledPlugins.Add(pluginInfo);

                            if (descriptor is IOverlayTypeDescriptor overlayDescriptor && pluginInfo.IsEnabled)
                            {
                                _registry.Register(overlayDescriptor);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load plugin assembly at {Path}", dllPath);
            }
        }
    }

    public void InstallPlugin(string sourceFilePath)
    {
        var ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        if (ext is ".sfplugin" or ".zip")
        {
            ExtractArchive(sourceFilePath);
        }
        else if (ext is ".dll")
        {
            var fileName = Path.GetFileName(sourceFilePath);
            var destPath = Path.Combine(_pluginsDirectory, fileName);
            File.Copy(sourceFilePath, destPath, overwrite: true);
        }
        LoadPlugins();
    }

    public event EventHandler<PluginInfo>? PluginUninstalled;

    public void UninstallPlugin(PluginInfo plugin)
    {
        try
        {
            PluginUninstalled?.Invoke(this, plugin);

            if (plugin.Descriptor is IOverlayTypeDescriptor overlayDesc)
            {
                _registry.Unregister(overlayDesc);
            }

            var pluginDir = Path.GetDirectoryName(plugin.AssemblyPath);
            if (pluginDir is not null && pluginDir != _pluginsDirectory && Directory.Exists(pluginDir))
            {
                Directory.Delete(pluginDir, recursive: true);
            }
            else if (File.Exists(plugin.AssemblyPath))
            {
                File.Delete(plugin.AssemblyPath);
            }
            InstalledPlugins.Remove(plugin);
            SavePluginStates();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to uninstall plugin {PluginId}", plugin.PluginId);
        }
    }

    private void ExtractArchive(string archivePath)
    {
        try
        {
            var pluginName = Path.GetFileNameWithoutExtension(archivePath);
            var targetDir = Path.Combine(_pluginsDirectory, pluginName);
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, recursive: true);
            }
            Directory.CreateDirectory(targetDir);
            ZipFile.ExtractToDirectory(archivePath, targetDir, overwriteFiles: true);

            // Delete original archive after successful extraction so we don't re-extract every launch
            File.Delete(archivePath);
            _logger.LogInformation("Successfully installed plugin archive {Archive} into {Dir}", archivePath, targetDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract plugin archive {Path}", archivePath);
        }
    }

    private HashSet<string> LoadDisabledPluginIds()
    {
        if (!File.Exists(_manifestPath)) return [];
        try
        {
            var json = File.ReadAllText(_manifestPath);
            var disabled = JsonSerializer.Deserialize<List<string>>(json);
            return disabled != null ? [.. disabled] : [];
        }
        catch
        {
            return [];
        }
    }

    private void SavePluginStates()
    {
        try
        {
            var disabledIds = InstalledPlugins.Where(p => !p.IsEnabled).Select(p => p.PluginId).ToList();
            var json = JsonSerializer.Serialize(disabledIds);
            File.WriteAllText(_manifestPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save plugin states manifest");
        }
    }
}
