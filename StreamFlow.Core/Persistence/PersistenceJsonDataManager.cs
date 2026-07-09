using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows.Media.Imaging;

using Newtonsoft.Json;

using StreamFlow.Core.AudioHandling;
using StreamFlow.Core.Data;
using StreamFlow.Core.Helpers;

namespace StreamFlow.Core.Persistence;

public class PersistenceJsonDataManager : IPersistenceDataManager
{
    private readonly string configurationFileName = $"StreamFlow_Settings.json";
    private readonly string importedSceneFolderName = "imported_scene_audio";

    private string ConfigurationFilePath => Path.Combine(AppDataPaths.RootFolder, configurationFileName);


    private readonly JsonSerializerSettings jsonSerializerSettings = new() {
        TypeNameHandling = TypeNameHandling.None,
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Ignore,
        PreserveReferencesHandling = PreserveReferencesHandling.None,
        DefaultValueHandling = DefaultValueHandling.Ignore,
        TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
        ObjectCreationHandling = ObjectCreationHandling.Auto
    };

    private readonly JsonSerializerSettings jsonSerializerExportSettings = new() {
        TypeNameHandling = TypeNameHandling.None,
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Ignore,
        PreserveReferencesHandling = PreserveReferencesHandling.None,
        DefaultValueHandling = DefaultValueHandling.Ignore,
        TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
        ObjectCreationHandling = ObjectCreationHandling.Auto
    };

    public PersistentData Load()
    {
        if (!File.Exists(ConfigurationFilePath))
        {
            return new();
        }

        var fileContent = File.ReadAllText(ConfigurationFilePath);

        PersistentData data = new();

        try
        {
            var loaded = JsonConvert.DeserializeObject<PersistentData>(fileContent, jsonSerializerSettings);
            if (loaded != null)
            {
                data = loaded;
                LoggerService.DebugLog(GetType(), "JSON Configuration file loaded");
                LoggerService.DebugLog(GetType(), $"{data}");
            }
        }
        catch (JsonSerializationException ex)
        {
            LoggerService.DebugLog(GetType(), "Error occoured while reading JSON");
            LoggerService.DebugLog(GetType(), ex.Message);
            LoggerService.DebugLog(GetType(), $"{ex.LineNumber}");
            LoggerService.DebugLog(GetType(), $"{ex.Data}");
        }

        return data;
    }

    public bool Save(PersistentData dataToSave)
    {
        try
        {
            File.WriteAllText(ConfigurationFilePath, JsonConvert.SerializeObject(dataToSave, Formatting.Indented, jsonSerializerSettings));
        }
        catch { }
        return true;
    }

    public async Task<bool> ExportScene(string exportFileNameWithPath, Scene sceneToExport)
    {
        var clonedScene = JsonConvert.DeserializeObject<Scene>(JsonConvert.SerializeObject(sceneToExport));

        List<string> filesToZip = [];

        if (clonedScene!.SceneAudioTrack != null)
        {
            filesToZip.Add(clonedScene.SceneAudioTrack.FilePath);
            clonedScene.SceneAudioTrack.FilePath = Path.GetFileName(clonedScene.SceneAudioTrack.FilePath);
        }

        foreach (var sfx in clonedScene.SceneSoundEffects)
        {
            filesToZip.Add(sfx.FilePath);
            sfx.FilePath = Path.GetFileName(sfx.FilePath);
        }

        try
        {
            using var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                // Remove duplicates
                filesToZip = [.. filesToZip.Distinct()];
                foreach (var file in filesToZip)
                {
                    var audioEntry = archive.CreateEntry(Path.GetFileName(file));

                    using var entryStream = audioEntry.Open();
                    using var fileStream = new FileStream(file, FileMode.Open);
                    await fileStream.CopyToAsync(entryStream);
                }

                var sceneFile = archive.CreateEntry("scene.json");

                using (var entryStream = sceneFile.Open())
                using (var streamWriter = new StreamWriter(entryStream))
                {
                    await streamWriter.WriteAsync(JsonConvert.SerializeObject(clonedScene, Formatting.Indented, jsonSerializerExportSettings));
                }

                if (sceneToExport.ImageSource != null)
                {
                    var backgroundEntry = archive.CreateEntry("background.png");
                    using var bgStream = backgroundEntry.Open();
                    BitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create((BitmapImage)sceneToExport.ImageSource));
                    encoder.Save(bgStream);
                }
            }

            using (var fileStream = new FileStream(exportFileNameWithPath, FileMode.Create))
            {
                memoryStream.Seek(0, SeekOrigin.Begin);
                await memoryStream.CopyToAsync(fileStream);
            }

        }
        catch (Exception ex)
        {
            Debug.WriteLine("Failed to export scene: Exception " + ex.Message);
            return false;
        }

        return true;
    }

    public async Task<Scene?> PeekScene(string fileName)
    {
        Scene? scene = null;

        using (var file = File.OpenRead(fileName))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
        {
            var sceneEntry = zip.GetEntry("scene.json");

            if (sceneEntry == null)
            {
                return null;
            }

            using var reader = new StreamReader(sceneEntry.Open());
            scene = JsonConvert.DeserializeObject<Scene>(reader.ReadToEnd());

            var backgroundEntry = zip.GetEntry("background.png");
            if (backgroundEntry != null)
            {
                using var zipStream = backgroundEntry.Open();
                using var memoryStream = new MemoryStream();
                await zipStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = memoryStream;
                bitmap.EndInit();
                if (bitmap.CanFreeze)
                {
                    bitmap.Freeze();
                }

                scene!.ImageSource = bitmap;
            }
        }

        return scene;
    }
    
    public async Task ImportScene(string packageFile, string? saveDirectory)
    {
        Scene? scene = null;

        if (saveDirectory == null)
        {
            saveDirectory = AppDataPaths.RootFolder + "\\" + importedSceneFolderName;
            Directory.CreateDirectory(saveDirectory);
        }

        using (var file = File.OpenRead(packageFile))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
        {

            var sceneEntry = zip.GetEntry("scene.json");
            using var reader = new StreamReader(sceneEntry!.Open());
            scene = JsonConvert.DeserializeObject<Scene>(reader.ReadToEnd());

            var backgroundEntry = zip.GetEntry("background.png");
            if (backgroundEntry != null)
            {
                using var zipStream = backgroundEntry.Open();
                using var memoryStream = new MemoryStream();
                await zipStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = memoryStream;
                bitmap.EndInit();
                if (bitmap.CanFreeze)
                {
                    bitmap.Freeze();
                }

                scene!.ImageSource = bitmap;
            }

            foreach (var entry in zip.Entries)
            {
                // Background and scene don't need to be unpacked
                if (entry.Name != "background.png" && entry.Name != "scene.json")
                {
                    using var zipStream = entry.Open();
                    using var fileStream = new FileStream(Path.Combine(saveDirectory, entry.Name), FileMode.Create);
                    await zipStream.CopyToAsync(fileStream);
                }
            }

            // Change path of audios to match relative directory
            if (scene!.SceneAudioTrack != null)
            {
                scene.SceneAudioTrack.FilePath = saveDirectory + "\\" + scene.SceneAudioTrack.FilePath;

                if (scene.SceneAudioTrack.FilePath.StartsWith(AppDataPaths.RootFolder))
                {
                    scene.SceneAudioTrack.FilePath = Path.GetRelativePath(AppDataPaths.RootFolder, scene.SceneAudioTrack.FilePath);
                }

                AppModel.Instance.Audios.Add(scene.SceneAudioTrack);
                AppModel.Instance.AddCategoryFromAudio(scene.SceneAudioTrack);
                AppModel.Instance.AddTagsFromAudio(scene.SceneAudioTrack);
            }

            foreach (var sfx in scene.SceneSoundEffects)
            {
                sfx.FilePath = saveDirectory + "\\" + sfx.FilePath;

                if (sfx.FilePath.StartsWith(AppDataPaths.RootFolder))
                {
                    sfx.FilePath = Path.GetRelativePath(AppDataPaths.RootFolder, sfx.FilePath);
                }

                AppModel.Instance.Audios.Add(sfx);
                AppModel.Instance.AddCategoryFromAudio(sfx);
                AppModel.Instance.AddTagsFromAudio(sfx);
            }
        }

        AppModel.Instance.SceneList.Add(scene);
    }

}
