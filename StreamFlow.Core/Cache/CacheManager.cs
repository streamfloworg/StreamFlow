using System.IO;
using System.Net;
using System.Windows.Media.Imaging;
using StreamFlow.Core.Data;
using StreamFlow.Core.Helpers;

namespace StreamFlow.Core.Cache;

public class CacheManager
{
    private static string IMAGE_CACHE_FOLDER_PATH { get; set; } = Path.Combine(AppDataPaths.RootFolder, "image_cache");

    public static string ImageCacheFolder => IMAGE_CACHE_FOLDER_PATH;

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
        Directory.CreateDirectory(IMAGE_CACHE_FOLDER_PATH);
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

        using var fileStream = new FileStream(IMAGE_CACHE_FOLDER_PATH + cacheID, FileMode.Create);
        BitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(fileStream);
    }

    public static void CleanUpCache()
    {
        var cachedFiles = Directory.GetFiles(IMAGE_CACHE_FOLDER_PATH);

        for (var i = 0; i < cachedFiles.Length; i++)
        {
            cachedFiles[i] = cachedFiles[i].Split('/').Last();
        }

        var filesToDelete = cachedFiles.ToList();

        foreach (var file in filesToDelete)
        {
            try
            {
                File.Delete(IMAGE_CACHE_FOLDER_PATH + file);
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
            if (!Directory.Exists(IMAGE_CACHE_FOLDER_PATH))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(IMAGE_CACHE_FOLDER_PATH))
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch { }
    }
}
