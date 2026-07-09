using System.Runtime.InteropServices;
using System.Windows;

using Microsoft.Win32;

namespace StreamFlow.App.Services;

/// <summary>Persists MainWindow's position/size/maximized-state across launches via the
/// registry (not the JSON settings file — this is pure OS-chrome placement, not app data, and
/// registry survives a settings-file reset cleanly). Guards against a saved position that no
/// longer intersects any connected monitor (e.g. a since-unplugged second display), falling
/// back to the window's own XAML defaults (centered, default size) rather than restoring
/// somewhere the user can't see or reach.</summary>
public static class WindowPlacementService
{
    private const string KeyPath = @"Software\StreamFlow\WindowPlacement";

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

    private const uint MONITOR_DEFAULTTONULL = 0;

    /// <summary>Applies a saved position/size if one exists and still intersects a connected
    /// monitor — call before the window is shown (e.g. from its constructor, after
    /// InitializeComponent) so there's no visible jump from the XAML-default placement.</summary>
    public static void Restore(Window window)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        if (key is null) return;

        var left = key.GetValue("Left") as int?;
        var top = key.GetValue("Top") as int?;
        var width = key.GetValue("Width") as int?;
        var height = key.GetValue("Height") as int?;
        var maximized = (key.GetValue("Maximized") as int?) == 1;

        if (left is null || top is null || width is null || height is null) return;
        if (width <= 0 || height <= 0) return;

        var rect = new RECT { Left = left.Value, Top = top.Value, Right = left.Value + width.Value, Bottom = top.Value + height.Value };
        if (MonitorFromRect(ref rect, MONITOR_DEFAULTTONULL) == IntPtr.Zero) return; // off-screen — keep XAML defaults

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = left.Value;
        window.Top = top.Value;
        window.Width = width.Value;
        window.Height = height.Value;
        if (maximized) window.WindowState = WindowState.Maximized;
    }

    /// <summary>Saves the window's current (or, if maximized/minimized, its restored) bounds —
    /// call from the window's Closing/Closed handler.</summary>
    public static void Save(Window window)
    {
        // RestoreBounds reflects where the window would sit if un-maximized/un-minimized, which
        // is what should be recalled on next launch rather than the maximized/minimized geometry.
        var bounds = window.WindowState == WindowState.Normal ? new Rect(window.Left, window.Top, window.Width, window.Height) : window.RestoreBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue("Left", (int)bounds.Left, RegistryValueKind.DWord);
        key.SetValue("Top", (int)bounds.Top, RegistryValueKind.DWord);
        key.SetValue("Width", (int)bounds.Width, RegistryValueKind.DWord);
        key.SetValue("Height", (int)bounds.Height, RegistryValueKind.DWord);
        key.SetValue("Maximized", window.WindowState == WindowState.Maximized ? 1 : 0, RegistryValueKind.DWord);
    }
}
