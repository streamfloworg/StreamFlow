using System;
using System.IO;

namespace StreamFlow.Core.Helpers;

/// <summary>Resolves the per-user folder for StreamFlow's own persisted state (settings JSON,
/// imported scene audio, cache, credential/token files, etc.), correctly distinguishing a
/// packaged (MSIX) install from an unpackaged one. Packaged apps must use the virtualized
/// per-package ApplicationData folder rather than raw LocalAppData, or Windows won't
/// isolate/allowlist the storage correctly for that package identity; unpackaged runs have no
/// package identity at all; ApplicationData.Current throws in that case.</summary>
public static class AppDataPaths
{
    private static readonly Lazy<string> _root = new(ResolveRoot);

    /// <summary>The per-user, per-app root folder for all of StreamFlow's persisted state —
    /// created if it doesn't exist yet. Previously several call sites anchored to
    /// AppContext.BaseDirectory (the built exe's own output folder) instead, which meant
    /// `dotnet clean`/rebuild silently wiped user settings between test sessions.</summary>
    public static string RootFolder => _root.Value;

    private static string ResolveRoot()
    {
        string path;
        try
        {
            // Throws when running unpackaged (no package identity) — the standard way to
            // detect packaged vs. unpackaged at runtime.
            path = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        catch
        {
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamFlow");
        }

        Directory.CreateDirectory(path);
        return path;
    }
}
