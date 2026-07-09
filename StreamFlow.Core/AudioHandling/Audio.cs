using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using StreamFlow.Core.AudioProperties;
using StreamFlow.Core.Contracts;
using StreamFlow.Core.Data;
using StreamFlow.Core.Helpers;

using SoundFlow.Metadata.Models;

namespace StreamFlow.Core.AudioHandling;

[JsonObject]
[JsonConverter(typeof(AudioTypeConverter))]
public abstract partial class Audio : ObservableObject, IAudio, IDisposable
{
    [JsonIgnore]
    public bool HasMetadata => !string.IsNullOrEmpty(FilePath.Trim()) && FileExtension.EndsWith(AppModel.Instance.ValidAudioExtensions, FilePath);

    [ObservableProperty]
    private SoundFormatInfo? _metadata;

    private StopMode stopMode = StopMode.Normal;

    private const float defaultFadeSpeed = 0f;

    [DefaultValue(10f)]
    private float fadeInSpeed = 0f;

    public event PropertyChangedEventHandler? PlaybackStateChanged;

    public float FadeInSpeed
    {
        get => fadeInSpeed;
        set => fadeInSpeed = value < 0f ? value : defaultFadeSpeed;
    }

    [DefaultValue(10f)]
    private float fadeOutSpeed = defaultFadeSpeed;

    public float FadeOutSpeed
    {
        get => fadeOutSpeed;
        set
        {
            fadeOutSpeed = value < 0f ? value : defaultFadeSpeed;
            OnPropertyChanged(nameof(FadeOutSpeed));
        }
    }


    public enum StopMode
    {
        Normal,
        Repeat,
        NextTrack,
        Force,
        SoftForce
    }


    private int hotKeyId = 0;

    private Hotkey? _hotKey;

    public Hotkey? Hotkey
    {
        get => _hotKey;
        set
        {
            _hotKey = value;
            if (_hotKey != null)
            {
                hotKeyId = HotKeyManager.RegisterHotKey(_hotKey);
            }
            else
            {
                if (hotKeyId != 0)
                {
                    HotKeyManager.UnregisterHotKey(hotKeyId);
                }
            }
            OnPropertyChanged(nameof(Hotkey));
        }
    }

    [ObservableProperty]
    [JsonConverter(typeof(StringEnumConverter))]
    private AudioTypes _audioType = AudioTypes.Unknown;

    /// <summary>
    /// Unique identifier for this audio item, used for URI protocol and deep linking
    /// </summary>
    /// <remarks>Automatically generated on first access if not set. Persists across sessions.</remarks>
    private string? _id;
    public string Id
    {
        get
        {
            if (string.IsNullOrEmpty(_id))
            {
                _id = GenerateShortId();
            }
            return _id;
        }
        set
        {
            _id = value;
            OnPropertyChanged(nameof(Id));
        }
    }

    /// <summary>
    /// Generates a short, URL-safe unique identifier (8 characters)
    /// </summary>
    private static string GenerateShortId(int length = 8)
    {
        // Generate a short ID using base64-encoded GUID (first 8 chars)
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "")
            [..length];
    }

    public void SetAudioType(AudioTypes type)
    {
        if (AudioType != type)
        {
            AudioType = type;
        }
    }

    private string filePath = string.Empty;

    /// <summary>
    /// Path to the audio file
    /// </summary>
    public string FilePath
    {
        get => filePath;
        set
        {
            filePath = value;
            OnPropertyChanged(nameof(FilePath));
            OnPropertyChanged(nameof(ValidPath));
            LoadMetadataAsync();
        }
    }

    partial void OnMetadataChanged(SoundFormatInfo? value)
    {
        if (value?.Tags?.AlbumArt != null && value.Tags.AlbumArt.Length > 0)
        {
            try
            {
                if (System.Windows.Application.Current != null)
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            var bmp = new BitmapImage();
                            using (var mem = new MemoryStream(value.Tags.AlbumArt))
                            {
                                bmp.BeginInit();
                                bmp.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                                bmp.CacheOption = BitmapCacheOption.OnLoad;
                                bmp.StreamSource = mem;
                                bmp.EndInit();
                            }
                            bmp.Freeze();
                            ImageSource = bmp;
                        }
                        catch (Exception)
                        {
                            // ignore
                        }
                    });
                }
            }
            catch (Exception)
            {
                // ignore
            }
        }
    }

    public void LoadMetadataAsync()
    {
        if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath) || Metadata != null) return;

        Task.Run(() =>
        {
            try
            {
                var metadata = AudioEngine.GetAudioMetadata(FilePath);
                if (metadata != null)
                {
                    if (System.Windows.Application.Current != null)
                    {
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                        {
                            Metadata = metadata;
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading metadata in background: {ex.Message}");
            }
        });
    }

    protected Audio()
    {
        _loopPoints ??= [];
    }

    [JsonIgnore]
    public bool ValidPath => File.Exists(FilePath);


    private Audio? next;

    /// <summary>
    /// Next audiotrack to be played after the current finished playing
    /// </summary>
    public Audio? Next
    {
        get => next;
        set
        {
            next = value;
            OnPropertyChanged(nameof(Next));
        }
    }

    private ImageSource? imageSource;

    [JsonIgnore]
    public ImageSource? ImageSource
    {
        get => imageSource;
        set
        {
            imageSource = value;
            OnPropertyChanged(nameof(ImageSource));
        }
    }

    private double volume = 0.25;

    /// <summary>
    /// Volume of the audio file
    /// </summary>
    public double Volume
    {
        get => volume;
        set => volume = value;
    }

    private bool repeat;

    /// <summary>
    /// If the audio file should repeat after it has finished playing
    /// </summary>
    public bool Repeat
    {
        get => repeat;
        set
        {
            repeat = value;
            OnPropertyChanged(nameof(Repeat));
        }
    }

    private string name = "No Audio Playing";

    /// <summary>
    /// The display name of the audio
    /// </summary>
    public string Name
    {
        get => name;
        set
        {
            name = value;
            OnPropertyChanged(nameof(Name));
        }
    }

    private ObservableCollection<AudioTag> tags = [];

    public ObservableCollection<AudioTag> Tags
    {
        get => tags;
        set
        {
            tags = value;
            OnPropertyChanged(nameof(Tags));
        }
    }

    private Category category = Category.Default;

    public Category Category
    {
        get => category;
        set
        {
            category = value;
            OnPropertyChanged(nameof(Category));
        }
    }


    private List<LoopPoint> _loopPoints = [];

    public List<LoopPoint> LoopPoints
    {
        get => _loopPoints;
        set => _loopPoints = value;
    }

    /// <summary>
    /// Creates a new Audio
    /// </summary>
    /// <param name="audioFile">Audio File Path</param>
    /// <param name="name">Audio Name</param>
    protected Audio(string filePath, string name)
    {
        FilePath = filePath;
        Name = name;
    }

    public override string ToString()
    {
        return $"Name: {Name} | Path: {FilePath}";
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
