using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Formats.Tar;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using MoreLinq.Extensions;

using Newtonsoft.Json;

using StreamFlow.Core.AudioHandling;
using StreamFlow.Core.AudioProperties;
using StreamFlow.Core.Cache;
using StreamFlow.Core.Helpers;

using SoundFlow.Structs;

using Application = System.Windows.Application;
using AudioEngine = StreamFlow.Core.AudioHandling.AudioEngine;
using Color = System.Windows.Media.Color;

namespace StreamFlow.Core.Data;

public partial class AppModel : ObservableObject, INotifyPropertyChanged
{
    private static AppModel? instance;
    private static readonly object _lock = new();

    /// <summary>
    /// Instance of the Singleton Implementation
    /// </summary>
    public static AppModel Instance
    {
        get
        {
            if (instance == null)
            {
                lock (_lock)
                {
                    if (instance == null)
                    {
                        instance = new AppModel();
                        instance.LoadData();
                    }
                }
            }
            return instance;
        }
    }

    public static event EventHandler? Loaded;

    [ObservableProperty]
    private ObservableCollection<Audio> audios = [];

    [ObservableProperty]
    private ObservableCollection<Scene> sceneList = [];

    [ObservableProperty]
    private ApplicationSettings settings = new();

    [ObservableProperty]
    private WindowOptions windowOptions = new();

    [ObservableProperty]
    private GoLiveSettings _goLiveSettings = new();

    [ObservableProperty]
    private ObservableCollection<AudioTag> tags = [];

    [ObservableProperty]
    private ObservableCollection<Category> categories = [];

    public List<FileExtension> ValidAudioExtensions =
    [
        new FileExtension("wav"),
        new FileExtension("aiff"),
        new FileExtension("flac"),
        new FileExtension("ogg"),
        new FileExtension("m4a"),
        new FileExtension("mp3"),
        new FileExtension("aac"),
    ];

    public List<FileExtension> ValidModuleExtensions =
    [
        new FileExtension("s3m"),
        new FileExtension("xm"),
        new FileExtension("it"),
        new FileExtension("mod"),
    ];

    public List<FileExtension> ValidImageExtensions =
    [
        new FileExtension("png"),
        new FileExtension("jpg"),
        new FileExtension("jpeg"),
        new FileExtension("webp"),
        new FileExtension("bmp")
    ];

    public List<FileExtension> ValidScenePackageExtensions =
    [
        new FileExtension("spas")
    ];

    public List<FileExtension> ValidCompositionExtenions =
    [
        new FileExtension("spac")
    ];

    private static bool loaded = false;

    public AppModel()
    {
    }

    public void SaveData()
    {
#if DEBUG
        if (DesignerProperties.GetIsInDesignMode(new DependencyObject()))
        {
            return;
        }
#endif
        var mainWindow = Window.GetWindow(Application.Current?.MainWindow) is not null ? Window.GetWindow(Application.Current.MainWindow) : null;
        while (mainWindow != null)
        {
            if (mainWindow.WindowState == WindowState.Normal)
            {
                WindowOptions.Height = mainWindow.Height;
                WindowOptions.Width = mainWindow.Width;
            }


            Persistence.PersistentData persistentData = new()
            {
                Categories = [.. Categories],
                Tags = [.. Tags],
                Audios = [.. Audios],
                SceneList = [.. SceneList],
                Settings = Settings,
                WindowOptions = WindowOptions,
                GoLiveSettings = GoLiveSettings
            };

            new Persistence.PersistenceJsonDataManager().Save(persistentData);
            break;
        }
    }

    private DispatcherTimer? _saveTimer;
    public void RequestSave(TimeSpan? delay = null)
    {
#if DEBUG
        if (DesignerProperties.GetIsInDesignMode(new DependencyObject()))
        {
            return;
        }
#endif
        var d = delay ?? TimeSpan.FromSeconds(5);

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            // Fallback: save immediately if no dispatcher
            SaveData();
            return;
        }
        else
        {
            _saveTimer ??= new DispatcherTimer(DispatcherPriority.Background, dispatcher);
            _saveTimer.Tick += (s, e) =>
            {
                _saveTimer.Stop();
                SaveData();
            };
            _saveTimer.Interval = d;
            _saveTimer.Stop();
            _saveTimer.Start();
        }
    }

    public void LoadData()
    {
        if (!loaded)
        {
            var data = new Persistence.PersistenceJsonDataManager().Load();
            Audios.Clear();
            SceneList.Clear();
            Tags.Clear();
            Categories.Clear();

            if (data != null)
            {
                loaded = true;
                data.Categories.ForEach(Categories.Add);
                data.Tags.ForEach(t => Tags.Add(t));
                data.Audios.ForEach(Audios.Add);
                data.SceneList.ForEach(SceneList.Add);
                Settings = data.Settings;
                WindowOptions = data.WindowOptions;
                GoLiveSettings = data.GoLiveSettings ?? new();

                // Migration check:
                var legacyPath = Path.Combine(AppDataPaths.RootFolder, "golive_settings.json");
                if (File.Exists(legacyPath))
                {
                    try
                    {
                        var json = File.ReadAllText(legacyPath);
                        var legacySettings = JsonConvert.DeserializeObject<GoLiveSettings>(json);
                        if (legacySettings != null)
                        {
                            // If the loaded settings had no scenes/slots, apply legacy scene migration
                            if (legacySettings.Scenes.Count == 0 && legacySettings.Slots is { Count: > 0 } legacySlots)
                            {
                                var scene = new SceneSettings { Name = legacySettings.SceneName ?? "Scene 1", Slots = legacySlots };
                                legacySettings.Scenes.Add(scene);
                                legacySettings.DefaultSceneId = scene.Id;
                            }
                            GoLiveSettings = legacySettings;
                        }
                        File.Delete(legacyPath);
                        SaveData(); // Save immediately in the unified file and remove the legacy file
                    }
                    catch (Exception ex)
                    {
                        LoggerService.DebugLog(GetType(), $"Error migrating legacy golive_settings: {ex.Message}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Change the SoundFlow output device
    /// </summary>
    /// <param name="outputDevice">Output Device number</param>
    public static void ChangeOutputDevice(DeviceInfo outputDevice)
    {
        AudioEngine.SetPlaybackDevice(outputDevice);
    }

    #region Category
    /// <summary>
    /// Add all non existing categories from a list of audios
    /// </summary>
    /// <param name="audios">The list of songs from which the category should be added</param>
    public void AddCategoryFromAudio(List<Audio> audios)
    {
        foreach (var audio in audios.Where(a => !a.Category.IsDefault()))
        {
            if (!Categories.Contains(audio.Category))
            {
                Categories.Add(audio.Category);
            }
        }
    }

    /// <summary>
    /// Add the category from a audio if it does not exist
    /// </summary>
    /// <param name="fromAudio">The audio from which the category should be added</param>
    public void AddCategoryFromAudio(Audio fromAudio)
    {
        AddCategoryFromAudio([fromAudio]);
    }

    /// <summary>
    /// Remove a category globally (Includes all audios)
    /// </summary>
    /// <param name="category">The category to remove</param>
    public void RemoveCategory(Category category)
    {
        Audios.Where(a => a.Category.Equals(category)).ToList().ForEach(a => a.Category = Category.Default);
        Categories.Remove(category);
    }

    public void ChangeCategoryColor(Category category, Color newColor)
    {
        if (!Categories.Contains(category))
        {
            return;
        }

        Categories.First(c => c.Equals(category)).Color = newColor;
    }
    #endregion

    #region Tag
    /// <summary>
    /// Add all non existing tags from a list of audios
    /// </summary>
    /// <param name="audios">The list of songs from which the tags should be added</param>
    public void AddTagsFromAudio(List<Audio> audios)
    {
        foreach (var audio in audios.Where(a => a.Tags.Count > 0))
        {
            foreach (var tag in audio.Tags)
            {
                if (!Tags.Contains(tag))
                {
                    Tags.Add(tag);
                }
            }
        }
    }

    /// <summary>
    /// Add the tags from a audio if it does not exist
    /// </summary>
    /// <param name="fromAudio">The audio from which the tags should be added</param>
    public void AddTagsFromAudio(Audio fromAudio) => AddTagsFromAudio([fromAudio]);

    /// <summary>
    /// Remove a tag globally (Includes all audios)
    /// </summary>
    /// <param name="tag">The tag to remove</param>
    public void RemoveTag(AudioTag tag)
    {
        foreach (var audio in Audios)
        {
            audio.Tags.Remove(tag);
        }

        Tags.Remove(tag);
    }
    #endregion

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
    }

    public void NotifyPropertyChanged([CallerMemberName] string? name = null)
    {
        if (name != null)
        {
            OnPropertyChanged(new PropertyChangedEventArgs(name));
            RequestSave();
        }
    }

    #region Stream Keys
    public Dictionary<string, string> LoadStreamKeys()
    {
        try
        {
            var path = Path.Combine(AppDataPaths.RootFolder, "stream_keys.dat");
            if (!File.Exists(path)) return [];
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(Encoding.UTF8.GetString(bytes)) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void SaveStreamKeys(Dictionary<string, string> keys)
    {
        try
        {
            var path = Path.Combine(AppDataPaths.RootFolder, "stream_keys.dat");
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(keys));
            File.WriteAllBytes(path, ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
        }
        catch { }
    }
    #endregion
}


