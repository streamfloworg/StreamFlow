using Microsoft.Extensions.DependencyInjection;

using StreamFlow.App.Services;

namespace StreamFlow.App.ViewModels.Pages;

/// <summary>Settings UI for StreamDeckServerService — enable/disable, port, and the API key the
/// StreamFlow.StreamDeck plugin needs pasted into its own config. The server itself is already
/// started (if enabled) by StreamDeckServerService's own IHostedService.StartAsync at app
/// launch; this partial only reacts to user-driven changes made afterward.</summary>
public partial class SettingsViewModel
{
    private readonly StreamDeckServerService _streamDeck = App.Services.GetRequiredService<StreamDeckServerService>();
    private bool _streamDeckInitialized;

    [ObservableProperty]
    private bool _isStreamDeckServerEnabled;

    [ObservableProperty]
    private int _streamDeckServerPort = 8080;

    [ObservableProperty]
    private string _streamDeckApiKey = "";

    [ObservableProperty]
    private string _streamDeckServerStatusText = "Stopped";

    private void InitializeStreamDeckSettings()
    {
        if (_streamDeckInitialized) return;
        _streamDeckInitialized = true;

        RefreshStreamDeckSettings();
    }

    public void RefreshStreamDeckSettings()
    {
        var saved = AppModel.Instance.GoLiveSettings;

        // If the hosted service already started the server at launch, its live state (in
        // particular, a freshly-generated key on a first-ever enable) is authoritative over
        // whatever `saved` still says.
        if (_streamDeck.IsRunning)
        {
            _isStreamDeckServerEnabled = true;
            _streamDeckServerPort = _streamDeck.Port;
            _streamDeckApiKey = _streamDeck.ApiKey;
        }
        else
        {
            _isStreamDeckServerEnabled = saved.IsStreamDeckServerEnabled;
            _streamDeckServerPort = saved.StreamDeckServerPort;
            _streamDeckApiKey = saved.StreamDeckApiKey ?? "";
        }

        UpdateStreamDeckStatusText();
        OnPropertyChanged(nameof(IsStreamDeckServerEnabled));
        OnPropertyChanged(nameof(StreamDeckServerPort));
        OnPropertyChanged(nameof(StreamDeckApiKey));
    }

    private void UpdateStreamDeckStatusText() =>
        StreamDeckServerStatusText = _streamDeck.IsRunning ? $"Running on http://127.0.0.1:{_streamDeck.Port}" : "Stopped";

    partial void OnIsStreamDeckServerEnabledChanged(bool value) => _ = ApplyStreamDeckServerStateAsync();

    partial void OnStreamDeckServerPortChanged(int value)
    {
        if (IsStreamDeckServerEnabled) _ = ApplyStreamDeckServerStateAsync();
        else SaveStreamDeckSettings();
    }

    private async Task ApplyStreamDeckServerStateAsync()
    {
        // Called fire-and-forget from the OnChanged hooks below (a property-changed hook can't
        // be awaited by its caller) — try/finally guarantees the enabled/port/key checkbox state
        // still gets persisted even if StartServerAsync's Kestrel setup throws, rather than an
        // unobserved task exception silently skipping SaveStreamDeckSettings entirely (which is
        // exactly what "the checkbox isn't being persisted" looks like from the UI).
        try
        {
            if (IsStreamDeckServerEnabled)
            {
                // Pass the existing key through (rather than null) so a port change on an
                // already-enabled server doesn't silently rotate the key out from under the plugin.
                await _streamDeck.StartServerAsync(StreamDeckServerPort, string.IsNullOrEmpty(StreamDeckApiKey) ? null : StreamDeckApiKey);
                StreamDeckApiKey = _streamDeck.ApiKey;
            }
            else
            {
                await _streamDeck.StopServerAsync();
            }

            UpdateStreamDeckStatusText();
        }
        catch (Exception ex)
        {
            StreamFlow.Core.Helpers.LoggerService.ErrorLog(GetType(), $"Stream Deck server state change failed: {ex.Message}");
        }
        finally
        {
            SaveStreamDeckSettings();
        }
    }

    [RelayCommand]
    private void RegenerateStreamDeckApiKey()
    {
        StreamDeckApiKey = _streamDeck.RegenerateApiKey();
        SaveStreamDeckSettings();
    }

    [RelayCommand]
    private void CopyStreamDeckApiKey()
    {
        if (!string.IsNullOrEmpty(StreamDeckApiKey))
            System.Windows.Clipboard.SetText(StreamDeckApiKey);
    }

    private void SaveStreamDeckSettings()
    {
        var saved = AppModel.Instance.GoLiveSettings;
        saved.IsStreamDeckServerEnabled = IsStreamDeckServerEnabled;
        saved.StreamDeckServerPort = StreamDeckServerPort;
        saved.StreamDeckApiKey = StreamDeckApiKey;
        AppModel.Instance.RequestSave();
    }
}
