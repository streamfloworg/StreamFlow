using System.IO;
using System.IO.Compression;
using System.Text.Json;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.Core.Helpers;

namespace StreamFlow.App.Services;

public sealed class SceneSetService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string SceneSetsRootPath => Path.Combine(AppDataPaths.RootFolder, "SceneSets");

    /// <summary>On-disk shape of metadata.json — wraps the scene list with the Scene Set's own
    /// Name/Author so both travel with the portable .sfset archive itself, not just the local
    /// SceneSetRegistration cache entry. Older .sfset files predate this wrapper and store a bare
    /// JSON array of scenes instead — see TryReadManifest, which falls back to that shape.</summary>
    private sealed class SceneSetManifest
    {
        public string Name { get; set; } = "";
        public string Author { get; set; } = "";
        public List<SceneSettings> Scenes { get; set; } = [];
    }

    /// <summary>Reads metadata.json in either the current wrapped-object shape or the older bare-
    /// array shape (pre-dating Name/Author), so Scene Sets created before this wrapper existed
    /// keep loading. Always returns a manifest — Name/Author are empty for the old shape, since
    /// nothing else on disk carries them.</summary>
    private static SceneSetManifest ReadManifest(string json)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<SceneSetManifest>(json, JsonOpts);
            if (manifest is not null) return manifest;
        }
        catch (JsonException) { /* fall through to the older bare-array shape below */ }

        var scenes = JsonSerializer.Deserialize<List<SceneSettings>>(json, JsonOpts) ?? [];
        return new SceneSetManifest { Scenes = scenes };
    }

    /// <summary>
    /// Exports the current scenes to a self-contained .sfset zip archive.
    /// Clones the settings first to avoid clobbering the live in-memory paths.
    /// </summary>
    public void ExportSceneSet(string zipPath, string name, string author, List<SceneSettings> scenes)
    {
        // 1. Clone the scenes so we can rewrite paths safely without clobbering the live layout
        var json = JsonSerializer.Serialize(scenes, JsonOpts);
        var clonedScenes = JsonSerializer.Deserialize<List<SceneSettings>>(json) ?? [];

        // 2. Create a temporary folder to collect files
        var tempDir = Path.Combine(Path.GetTempPath(), $"streamflow_export_{Guid.NewGuid():N}");
        var assetsDir = Path.Combine(tempDir, "assets");
        Directory.CreateDirectory(tempDir);

        try
        {
            // 3. Copy referenced asset files to the assets folder and rewrite paths to be relative
            foreach (var scene in clonedScenes)
            {
                foreach (var slot in scene.Slots)
                {
                    if (!slot.IsOverlay) continue;

                    if (slot.OverlayKind == OverlayKind.Image && !string.IsNullOrEmpty(slot.ImagePath))
                    {
                        if (File.Exists(slot.ImagePath))
                        {
                            Directory.CreateDirectory(assetsDir);
                            var destName = Path.GetFileName(slot.ImagePath);
                            var destPath = Path.Combine(assetsDir, destName);
                            File.Copy(slot.ImagePath, destPath, overwrite: true);
                            slot.ImagePath = $"assets/{destName}";
                        }
                    }
                    else if (slot.OverlayKind == OverlayKind.Video && !string.IsNullOrEmpty(slot.VideoPath))
                    {
                        if (File.Exists(slot.VideoPath))
                        {
                            Directory.CreateDirectory(assetsDir);
                            var destName = Path.GetFileName(slot.VideoPath);
                            var destPath = Path.Combine(assetsDir, destName);
                            File.Copy(slot.VideoPath, destPath, overwrite: true);
                            slot.VideoPath = $"assets/{destName}";
                        }
                    }
                }
            }

            // 4. Write metadata.json to the temp folder
            var manifest = new SceneSetManifest { Name = name, Author = author, Scenes = clonedScenes };
            var metadataPath = Path.Combine(tempDir, "metadata.json");
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(manifest, JsonOpts));

            // 5. Create ZIP archive
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }
            ZipFile.CreateFromDirectory(tempDir, zipPath);
        }
        finally
        {
            // 6. Cleanup temporary directory
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch (IOException) { }
        }
    }

    /// <summary>
    /// Imports a .sfset archive by extracting it to a unique cache directory in LocalAppData.
    /// </summary>
    public SceneSetRegistration ImportSceneSet(string zipPath)
    {
        var id = Guid.NewGuid().ToString("N");
        var extractPath = Path.Combine(SceneSetsRootPath, id);
        Directory.CreateDirectory(extractPath);

        ZipFile.ExtractToDirectory(zipPath, extractPath);

        var metadataPath = Path.Combine(extractPath, "metadata.json");
        if (!File.Exists(metadataPath))
        {
            throw new FileNotFoundException("Missing metadata.json in the Scene Set archive.");
        }

        // Older .sfset files (bare-array metadata.json) never carried a Name, so this fallback
        // stays for those — new exports always have a real Name in the manifest.
        var manifest = ReadManifest(File.ReadAllText(metadataPath));
        var name = string.IsNullOrWhiteSpace(manifest.Name) ? Path.GetFileNameWithoutExtension(zipPath) : manifest.Name;

        return new SceneSetRegistration
        {
            Id = id,
            Name = name,
            Author = manifest.Author,
            ExtractPath = extractPath
        };
    }

    /// <summary>
    /// Loads a Scene Set's layout from the cached directory, resolving all relative paths to absolute paths.
    /// </summary>
    public List<SceneSettings> LoadSceneSetLayout(SceneSetRegistration reg)
    {
        var metadataPath = Path.Combine(reg.ExtractPath, "metadata.json");
        if (!File.Exists(metadataPath)) return [];

        var scenes = ReadManifest(File.ReadAllText(metadataPath)).Scenes;

        // Resolve relative paths back to absolute cached paths
        foreach (var scene in scenes)
        {
            foreach (var slot in scene.Slots)
            {
                if (!slot.IsOverlay) continue;

                if (slot.OverlayKind == OverlayKind.Image && !string.IsNullOrEmpty(slot.ImagePath) && slot.ImagePath.StartsWith("assets/"))
                {
                    slot.ImagePath = Path.GetFullPath(Path.Combine(reg.ExtractPath, slot.ImagePath));
                }
                else if (slot.OverlayKind == OverlayKind.Video && !string.IsNullOrEmpty(slot.VideoPath) && slot.VideoPath.StartsWith("assets/"))
                {
                    slot.VideoPath = Path.GetFullPath(Path.Combine(reg.ExtractPath, slot.VideoPath));
                }
            }
        }

        return scenes;
    }

    /// <summary>
    /// Saves the current scenes layout to the Scene Set cache, making all local asset paths relative.
    /// </summary>
    public void SaveSceneSetLayout(SceneSetRegistration reg, List<SceneSettings> scenes)
    {
        var json = JsonSerializer.Serialize(scenes, JsonOpts);
        var clonedScenes = JsonSerializer.Deserialize<List<SceneSettings>>(json) ?? [];

        foreach (var scene in clonedScenes)
        {
            foreach (var slot in scene.Slots)
            {
                if (!slot.IsOverlay) continue;

                // Rewrite absolute cached paths back to relative paths for zip portability
                if (slot.OverlayKind == OverlayKind.Image && !string.IsNullOrEmpty(slot.ImagePath) && slot.ImagePath.Contains(reg.ExtractPath))
                {
                    var filename = Path.GetFileName(slot.ImagePath);
                    slot.ImagePath = $"assets/{filename}";
                }
                else if (slot.OverlayKind == OverlayKind.Video && !string.IsNullOrEmpty(slot.VideoPath) && slot.VideoPath.Contains(reg.ExtractPath))
                {
                    var filename = Path.GetFileName(slot.VideoPath);
                    slot.VideoPath = $"assets/{filename}";
                }
            }
        }

        var manifest = new SceneSetManifest { Name = reg.Name, Author = reg.Author, Scenes = clonedScenes };
        var metadataPath = Path.Combine(reg.ExtractPath, "metadata.json");
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(manifest, JsonOpts));
    }

    /// <summary>
    /// Exports an arbitrary registered Scene Set's cached layout directly, without needing it
    /// loaded into the live editor first — reads its own cached files, unlike ExportSceneSet
    /// (called with whatever the in-memory editor's Scenes/ActiveScene currently hold).
    /// </summary>
    public void ExportRegisteredSceneSet(SceneSetRegistration reg, string zipPath) =>
        ExportSceneSet(zipPath, reg.Name, reg.Author, LoadSceneSetLayout(reg));

    /// <summary>
    /// Deletes the cached directory and files for a Scene Set.
    /// </summary>
    public void UninstallSceneSet(SceneSetRegistration reg)
    {
        try
        {
            if (Directory.Exists(reg.ExtractPath))
            {
                Directory.Delete(reg.ExtractPath, recursive: true);
            }
        }
        catch (IOException) { }
    }
}
