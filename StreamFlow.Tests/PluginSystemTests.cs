using System.IO;

using Microsoft.Extensions.Logging.Abstractions;

using StreamFlow.App.Services.Overlays;
using StreamFlow.App.Services.Overlays.Plugins;
using StreamFlow.Core.Data;
using StreamFlow.Plugin.Marquee;
using StreamFlow.Plugin.SDK;

using Xunit;

namespace StreamFlow.Tests;

public class PluginSystemTests
{
    [Fact]
    public void MarqueePluginDescriptor_MetadataAndContracts_AreValid()
    {
        var descriptor = new MarqueePluginDescriptor();

        Assert.Equal("streamflow.plugin.marquee", descriptor.PluginId);
        Assert.Equal("marquee", descriptor.TypeKey);
        Assert.Equal(OverlayKind.Custom, descriptor.Kind);
        Assert.True(descriptor.HasConfiguration);
        Assert.Equal(OverlaySessionMode.StaticPixels, descriptor.SessionMode);
    }

    [Fact]
    public void MarqueePluginDescriptor_RenderStaticBgra_ProducesValidPixels()
    {
        var descriptor = new MarqueePluginDescriptor();
        var content = (MarqueeOverlayContent)descriptor.CreateDefault();
        content.MarqueeText = "TEST BANNER";

        var result = descriptor.RenderStaticBgra(content, null);

        Assert.NotNull(result);
        var (width, height, pixels) = result.Value;
        Assert.Equal(1280, width);
        Assert.Equal(100, height);
        Assert.Equal(1280 * 100 * 4, pixels.Length);
    }

    [Fact]
    public void MarqueePluginDescriptor_SerializeAndDeserialize_RoundTrips()
    {
        var descriptor = new MarqueePluginDescriptor();
        var content = new MarqueeOverlayContent
        {
            MarqueeText = "Custom Banner Text",
            BackgroundColorHex = "#FF00FF"
        };

        var slotSettings = new SlotSettings();
        descriptor.Serialize(content, slotSettings);

        Assert.Equal(OverlayKind.Custom, slotSettings.OverlayKind);
        Assert.Equal("marquee", slotSettings.OverlayTypeKey);
        Assert.Equal("Custom Banner Text", slotSettings.OverlayText);
        Assert.Equal("#FF00FF", slotSettings.OverlayColorHex);

        var restored = descriptor.Deserialize(slotSettings) as MarqueeOverlayContent;
        Assert.NotNull(restored);
        Assert.Equal("Custom Banner Text", restored.MarqueeText);
        Assert.Equal("#FF00FF", restored.BackgroundColorHex);
    }

    [Fact]
    public void PluginManagerService_FullLifecycle_InstallLoadDisableUninstall()
    {
        // 1. Setup isolated test environment
        var tempDir = Path.Combine(Path.GetTempPath(), $"sf_plugin_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var logger = NullLogger<PluginManagerService>.Instance;
            var registry = new OverlayTypeRegistry();
            var manager = new PluginManagerService(logger, registry, tempDir);

            // Locate built plugin DLL or .sfplugin package
            var marqueeDllPath = typeof(MarqueePluginDescriptor).Assembly.Location;
            Assert.True(File.Exists(marqueeDllPath), "Marquee plugin assembly file must exist for full flow test.");

            // 2. Install Plugin
            manager.InstallPlugin(marqueeDllPath);

            Assert.Single(manager.InstalledPlugins);
            var pluginInfo = manager.InstalledPlugins[0];
            Assert.Equal("streamflow.plugin.marquee", pluginInfo.PluginId);
            Assert.True(pluginInfo.IsEnabled);

            // Verify registered in OverlayTypeRegistry
            var registeredDescriptor = registry.GetByKey("marquee");
            Assert.NotNull(registeredDescriptor);
            Assert.Equal("Marquee Banner", registeredDescriptor.DisplayName);

            // 3. Disable Plugin and verify persistence
            pluginInfo.IsEnabled = false;

            var newRegistry = new OverlayTypeRegistry();
            var newManager = new PluginManagerService(logger, newRegistry, tempDir);
            newManager.LoadPlugins();

            Assert.Single(newManager.InstalledPlugins);
            var reloadedPlugin = newManager.InstalledPlugins[0];
            Assert.False(reloadedPlugin.IsEnabled);
            Assert.Null(newRegistry.GetByKey("marquee")); // Disabled plugin must not register descriptor

            // 4. Uninstall Plugin
            newManager.UninstallPlugin(reloadedPlugin);
            Assert.Empty(newManager.InstalledPlugins);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }
}
