using System.IO;
using System.Net;
using System.Windows.Media.Imaging;
using StreamFlow.Core.Data;
using StreamFlow.Core.Helpers;

namespace StreamFlow.Core.Cache;

public class CacheManager
{
    private static string CACHE_FOLDER_PATH { get; set; } = Path.Combine(AppDataPaths.RootFolder, "cache");
    private static string VISUALIZATION_CACHE_FOLDER_PATH { get; set; } = Path.Combine(CACHE_FOLDER_PATH, "visualizations");
    private static string AUTH_CACHE_FOLDER_PATH { get; set; } = Path.Combine(CACHE_FOLDER_PATH, "auth");

    public static string CacheFolder => CACHE_FOLDER_PATH;
    public static string VisualizationCacheFolder => VISUALIZATION_CACHE_FOLDER_PATH;
    public static string AuthCacheFolder => AUTH_CACHE_FOLDER_PATH;

    private static CacheManager? instance;

    /// <summary>
    /// Instance of the Singleton Implementation
    /// </summary>
    public static CacheManager Instance
    {
        get
        {
            instance ??= new CacheManager();

            return instance;
        }
    }

    public CacheManager()
    {
        Directory.CreateDirectory(CACHE_FOLDER_PATH);
        Directory.CreateDirectory(AUTH_CACHE_FOLDER_PATH);
        Directory.CreateDirectory(VISUALIZATION_CACHE_FOLDER_PATH);
    }

    public static string GetNewCacheID()
    {
        var newID = Guid.NewGuid().ToString("n");
        return newID;
    }

    public static void SaveAuthToCache(CookieContainer cookie, string cacheID)
    {
        if (cookie == null)
        {
            return;
        }

    }

    public static void SaveImageToCache(BitmapSource source, string cacheID)
    {
        if (source == null)
        {
            return;
        }

        using var fileStream = new FileStream(VISUALIZATION_CACHE_FOLDER_PATH + cacheID, FileMode.Create);
        BitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(fileStream);
    }

    public static void CleanUpCache()
    {
        var cachedFiles = Directory.GetFiles(VISUALIZATION_CACHE_FOLDER_PATH);

        for (var i = 0; i < cachedFiles.Length; i++)
        {
            cachedFiles[i] = cachedFiles[i].Split('/').Last();
        }

        var filesToDelete = cachedFiles.ToList();

        foreach (var file in filesToDelete)
        {
            try
            {
                File.Delete(VISUALIZATION_CACHE_FOLDER_PATH + file);
            }
            catch (Exception)
            {
                // A file may be still in use or is blocked by something else idk
            }
        }
    }

    public static void ClearAllImages()
    {
        try
        {
            if (!Directory.Exists(VISUALIZATION_CACHE_FOLDER_PATH))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(VISUALIZATION_CACHE_FOLDER_PATH))
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch { }
    }
}
