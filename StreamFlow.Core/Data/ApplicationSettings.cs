using CommunityToolkit.Mvvm.ComponentModel;

using Newtonsoft.Json;

using StreamFlow.Core.AudioProperties;

using System.Text.Json.Serialization;

namespace StreamFlow.Core.Data;

[JsonObject]
public partial class ApplicationSettings : ObservableObject
{

    private bool extendedModeEnabled = false;

    public bool ExtendedModeEnabled
    {
        get => extendedModeEnabled;
        set
        {
            extendedModeEnabled = value;
            OnPropertyChanged();
        }
    }

    private double defaultAudioTrackVolume = 0.25;

    public double DefaultAudioTrackVolume
    {
        get => defaultAudioTrackVolume;
        set
        {
            if (value > 1)
            {
                value = 1;
            }

            defaultAudioTrackVolume = value;
            OnPropertyChanged();
        }
    }

    private double defaultSoundEffectVolume = 0.35;

    public double DefaultSoundEffectVolume
    {
        get => defaultSoundEffectVolume;
        set
        {
            if (value > 1)
            {
                value = 1;
            }

            defaultSoundEffectVolume = value;
            OnPropertyChanged();
        }
    }

    private string _outputDevice = string.Empty;

    public string OutputDevice
    {
        get => _outputDevice;
        set
        {
            if (value != _outputDevice)
            {
                _outputDevice = value;
                OnPropertyChanged();
            }
        }
    }

    private string _captureDevice = string.Empty;

    public string CaptureDevice
    {
        get => _captureDevice;
        set
        {
            if (value != _captureDevice)
            {
                _captureDevice = value;
                OnPropertyChanged();
            }
        }
    }

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    private AudioViewType selectedView = AudioViewType.GridView;

    public AudioViewType SelectedView
    {
        get => selectedView;
        private set
        {
            selectedView = value;
            OnPropertyChanged();
        }
    }

    public void SetView(AudioViewType viewId)
    {
        SelectedView = viewId;
    }

    private bool fadeAudioOnPause = false;

    public bool FadeAudioOnPause
    {
        get => fadeAudioOnPause;
        set
        {
            fadeAudioOnPause = value;
            OnPropertyChanged();
        }
    }

    private bool fadeSoundEffectsOnStop = false;

    public bool FadeSoundEffectsOnStop
    {
        get => fadeSoundEffectsOnStop;
        set
        {
            fadeSoundEffectsOnStop = value;
            OnPropertyChanged();
        }
    }

    private bool useFullHeightForSceneBackground = false;

    public bool UseFullHeightForSceneBackground
    {
        get => useFullHeightForSceneBackground;
        set
        {
            useFullHeightForSceneBackground = value;
            OnPropertyChanged();
        }
    }

    private FilterOptions? filterOptions;

    public FilterOptions FilterOptions
    {
        get
        {
            filterOptions ??= new FilterOptions();
            return filterOptions;
        }
        set
        {
            filterOptions = value; OnPropertyChanged();
        }
    }


    private Hotkey stopAllAudioHotKey;

    public Hotkey StopAllAudioHotKey
    {
        get => stopAllAudioHotKey;
        set
        {
            stopAllAudioHotKey = value; OnPropertyChanged();
        }
    }

    private string preferredTheme = "Default";

    public string PreferredTheme
    {
        get => preferredTheme;
        set
        {
            if (!string.Equals(preferredTheme, value, StringComparison.OrdinalIgnoreCase))
            {
                preferredTheme = value;
                OnPropertyChanged();
            }
        }
    }

    public ApplicationSettings()
    {
    }
}



