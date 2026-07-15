using System.Collections.ObjectModel;
using System.Globalization;

using StreamFlow.App.Services;
using StreamFlow.App.Services.Core;

namespace StreamFlow.App.ViewModels.Pages;

/// <summary>Streaming profiles, OAuth connect flows, bitrate/fps/encoder settings, start/stop
/// stream, and scene-set registration/import/export/bake — everything tied to "which service
/// am I publishing to and with what settings," as opposed to the shared SceneEditorViewModel's
/// "what does the layout look like." Also owns loading a Scene Set's raw scenes into the shared
/// SceneEditor — that's a streaming-profile concept (which named set is linked to which
/// profile), not a pure editing concern.</summary>
public sealed record EncoderOption(string Code, string Name);
public sealed record QualityPreset(string Name, uint BitrateKbps, uint Fps);

public partial class GoLiveViewModel
{
    public static readonly EncoderOption[] EncoderOptions = [
        new EncoderOption("libx264", "Standard CPU (Software - x264)"),
        new EncoderOption("h264_nvenc", "NVIDIA NVENC (Hardware Accelerated)"),
        new EncoderOption("h264_amf", "AMD AMF (Hardware Accelerated)"),
        new EncoderOption("h264_qsv", "Intel QuickSync (Hardware Accelerated)")
    ];
    public static StreamServiceKind[] ServiceKindOptions { get; } = [StreamServiceKind.Twitch, StreamServiceKind.YouTube, StreamServiceKind.Custom];

    /// <summary>Short tally-pill label: "Idle", "Live", or "Error".</summary>
    [ObservableProperty]
    private string _statusLabel = "Idle";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private float _liveFps;

    [ObservableProperty]
    private uint _liveBitrateKbps;

    [ObservableProperty]
    private ulong _droppedFrames;

    [ObservableProperty]
    private bool _isStreaming;

    /// <summary>Whether the *currently active* stream (if any) was started via "Test Stream"
    /// rather than "Go Live" — only meaningful while <see cref="IsStreaming"/> is true; reset to
    /// false whenever a stream stops (see the StreamStoppedEvent handler in GoLiveViewModel.cs),
    /// so a real "Go Live" afterward doesn't inherit a stale test-mode label.</summary>
    [ObservableProperty]
    private bool _isTestStreaming;

    /// <summary>Toggle bound to the "Test Mode" switch beside the Go Live button — while on, the
    /// button reads "Test Stream" and starting it goes through the same bandwidth-test path as
    /// before (see StartStreamCoreAsync's testMode param), just without a dedicated second
    /// button. Not persisted: always starts back at "off" (real Go Live) on launch.</summary>
    [ObservableProperty]
    private bool _isTestModeEnabled;

    /// <summary>Per-service gate for the Test Mode toggle. Deliberately an explicit allow-list,
    /// not a blanket "anything but X" check: "test mode" means something different per platform
    /// — Twitch honors a "?bandwidthtest=true" query param on the stream key (see
    /// StreamingProfile.BuildRtmpUrl), YouTube has no such flag and instead needs a whole
    /// ephemeral unlisted broadcast created via the Live Streaming API (see
    /// StartStreamCoreAsync + YouTubeAuthService.CreateTestBroadcastAsync/EndTestBroadcastAsync).
    /// A newly added StreamServiceKind should default to unsupported (fall through to the `_`
    /// case) until it has its own real implementation here — the alternative (defaulting new
    /// platforms to "supported") is exactly what let Test Mode silently misbehave for YouTube
    /// before this existed. Custom is unsupported for the same reason: an arbitrary
    /// user-configured RTMP endpoint has no known test convention we can safely assume.</summary>
    public bool IsTestModeSupported => ActiveProfile?.ServiceKind switch
    {
        StreamServiceKind.Twitch => true,
        StreamServiceKind.YouTube => true,
        _ => false,
    };

    public bool IsTestModeSelectorVisible => IsTestModeSupported && !IsRecordOnlyMode;

    /// <summary>Set by StartStreamCoreAsync when a YouTube test stream creates an ephemeral
    /// unlisted broadcast, so EndActiveYouTubeTestBroadcastIfAny (called from the
    /// StreamStoppedEvent handler in GoLiveViewModel.cs) knows what to tear down — that handler
    /// fires however the stream actually ended (explicit stop, core-side error, disconnect), not
    /// just the happy-path Stop click, so tracking it as a field rather than a local is what
    /// makes cleanup reliable.</summary>
    private string? _activeYouTubeTestBroadcastId;

    /// <summary>Safety gate: while streaming, slot drag/resize is disabled by default so an
    /// accidental drag doesn't change what's live — must be explicitly unlocked. Not persisted,
    /// and always re-locks the next time streaming starts.</summary>
    [ObservableProperty]
    private bool _isLiveEditUnlocked;

    /// <summary>Whether slots can currently be dragged/resized: always true while idle,
    /// gated by <see cref="IsLiveEditUnlocked"/> while streaming.</summary>
    public bool CanEditSlots => !IsStreaming || IsLiveEditUnlocked;

    public ObservableCollection<StreamingProfile> Profiles { get; } = [];

    private readonly StreamingProfile _noneProfile = new("none", "None")
    {
        ServiceKind = StreamServiceKind.Custom,
        ServerUrl = "",
        StreamKey = "",
        BitrateKbps = 6000,
        Fps = 30,
        Encoder = "libx264"
    };

    private StreamingProfile? _activeProfile;
    public StreamingProfile? ActiveProfile
    {
        get => _activeProfile;
        set
        {
            if (ReferenceEquals(_activeProfile, value)) return;

            if (HasUnsavedChanges)
            {
                _ = PromptAndSwitchProfileAsync(value);
                return;
            }

            _activeProfile = value;
            OnPropertyChanged(nameof(ActiveProfile));
            OnActiveProfileChanged(value);
        }
    }

    private List<SceneSettings> _localDefaultScenes = [];

    public static readonly QualityPreset[] QualityPresets = [
        new QualityPreset("High (1080p 60fps - 6000 kbps)", 6000, 60),
        new QualityPreset("Medium (1080p 30fps - 4500 kbps)", 4500, 30),
        new QualityPreset("Standard (720p 60fps - 3500 kbps)", 3500, 60),
        new QualityPreset("Low (720p 30fps - 2500 kbps)", 2500, 30),
        new QualityPreset("Custom (Manual configuration)", 0, 0)
    ];

    [ObservableProperty]
    private QualityPreset _selectedQualityPreset = QualityPresets[0];

    [ObservableProperty]
    private uint _bitrateKbps = 6000;

    [ObservableProperty]
    private uint _fps = 30;

    [ObservableProperty]
    private string _encoder = EncoderOptions[0].Code;

    private bool _isUpdatingPreset;

    partial void OnSelectedQualityPresetChanged(QualityPreset value)
    {
        if (_isUpdatingPreset) return;
        _isUpdatingPreset = true;
        try
        {
            if (value.BitrateKbps > 0)
            {
                BitrateKbps = value.BitrateKbps;
                Fps = value.Fps;
            }
        }
        finally
        {
            _isUpdatingPreset = false;
        }
        OnPropertyChanged(nameof(IsCustomQualityActive));
    }

    public bool IsCustomQualityActive => SelectedQualityPreset?.BitrateKbps == 0;

    private void SyncQualityPresetSelection()
    {
        if (_isUpdatingPreset) return;
        _isUpdatingPreset = true;
        try
        {
            var match = QualityPresets.FirstOrDefault(p => p.BitrateKbps == BitrateKbps && p.Fps == Fps);
            SelectedQualityPreset = match ?? QualityPresets[^1];
        }
        finally
        {
            _isUpdatingPreset = false;
        }
        OnPropertyChanged(nameof(IsCustomQualityActive));
    }

    partial void OnBitrateKbpsChanged(uint value)
    {
        if (ActiveProfile is not null) ActiveProfile.BitrateKbps = value;
        ScheduleSaveSettings();
        SyncQualityPresetSelection();
    }

    partial void OnFpsChanged(uint value)
    {
        if (ActiveProfile is not null) ActiveProfile.Fps = value;
        ScheduleSaveSettings();
        SyncQualityPresetSelection();
    }

    partial void OnEncoderChanged(string value)
    {
        if (ActiveProfile is not null) ActiveProfile.Encoder = value;
        ScheduleSaveSettings();
    }

    /// <summary>"Show Preview" in the UI — one switch driving both halves of the Spout2
    /// Integration Plan: publishes the composited output as a named Spout2 source for other apps
    /// on this machine (OBS + obs-spout2-plugin, TouchDesigner, Resolume, the official
    /// SpoutReceiver demo) to pick up (Option A), and — since the same shared texture already
    /// exists once this is on — GoLiveView.xaml.cs also opens it directly into a D3DImage for the
    /// primary preview instead of the CPU pipe+WriteableBitmap path (Option B; see
    /// SpoutPreviewRenderer). Independent of streaming, can be on while idle. See
    /// native/crates/core/src/spout.rs and the Spout2 Integration Plan in the Obsidian vault.</summary>
    [ObservableProperty]
    private bool _isSpoutOutputEnabled;

    [ObservableProperty]
    private string _spoutSenderName = "StreamFlow";

    partial void OnIsSpoutOutputEnabledChanged(bool value)
    {
        _ = _core.SendCommandAsync(new SetSpoutOutputCommand(value, SpoutSenderName));
        ScheduleSaveSettings();
    }

    partial void OnSpoutSenderNameChanged(string value)
    {
        if (IsSpoutOutputEnabled) _ = _core.SendCommandAsync(new SetSpoutOutputCommand(true, value));
        ScheduleSaveSettings();
    }

    private void OnActiveProfileChanged(StreamingProfile? value)
    {
        if (value is not null)
        {
             _bitrateKbps = value.BitrateKbps;
             _fps = value.Fps;
             _encoder = value.Encoder ?? "libx264";
             SyncQualityPresetSelection();

             if (value.ServiceKind == StreamServiceKind.Twitch)
                 _ = RestoreTwitchSessionAsync();
             else if (value.ServiceKind == StreamServiceKind.YouTube)
                 _ = RestoreYouTubeSessionAsync();

             if (value.IsConnected)
                 _ = FetchAndApplyChannelMetadataAsync();
        }

        OnPropertyChanged(nameof(BitrateKbps));
        OnPropertyChanged(nameof(Fps));
        OnPropertyChanged(nameof(Encoder));

        OnPropertyChanged(nameof(IsTestModeSupported));
        OnPropertyChanged(nameof(IsTestModeSelectorVisible));
        if (!IsTestModeSupported) IsTestModeEnabled = false;

        UpdateChatConnection();
        RefreshStartStreamAvailability();
        RemoveProfileCommand.NotifyCanExecuteChanged();
        BakeOverridesToSceneSetCommand.NotifyCanExecuteChanged();
    }

    private async Task PromptAndSwitchProfileAsync(StreamingProfile? newProfile)
    {
        var result = await _dialogs.PromptUnsavedChangesAsync(
            "Unsaved Changes",
            "You have unsaved changes in your layout. Would you like to save them before switching profiles?");

        if (result == "cancel")
        {
            OnPropertyChanged(nameof(ActiveProfile));
            return;
        }

        if (result == "save")
        {
            SaveSceneSet();
        }

        HasUnsavedChanges = false;
        _activeProfile = newProfile;
        OnPropertyChanged(nameof(ActiveProfile));
        OnActiveProfileChanged(newProfile);
    }

    [RelayCommand]
    private void SaveSceneSet()
    {
        var sceneSetId = SceneEditor.ActiveSceneSet?.Id ?? "default";

        var scenesSnapshot = SceneEditor.Scenes.Select(scene => new SceneSettings
        {
            Id = scene.Id,
            Name = scene.Name,
            CanvasResolutionWidth = scene.CanvasResolutionWidth,
            CanvasResolutionHeight = scene.CanvasResolutionHeight,
            SwitchHotkeyKey = scene.SwitchHotkey?.Key.ToString(),
            SwitchHotkeyModifiers = scene.SwitchHotkey?.Modifiers.ToString(),
            Slots = scene.Slots.Select(s => new SlotSettings
            {
                IsPrimary = s.IsPrimary,
                IsOverlay = s.IsOverlay,
                OverlayKind = s.OverlayKind,
                SourceId = s.SourceId,
                DisplayName = s.DisplayName,
                ImagePath = (s.Content as ImageOverlayContent)?.ImagePath,
                OverlayText = (s.Content as TextOverlayContent)?.OverlayText,
                OverlayColorHex = (s.Content as ColorOverlayContent)?.OverlayColor?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                VideoPath = (s.Content as VideoOverlayContent)?.VideoPath,
                XPercent = s.XPercent,
                YPercent = s.YPercent,
                WPercent = s.WPercent,
                HPercent = s.HPercent,
                CornerRadiusPercent = s.CornerRadiusPercent,
                BlurRadius = (s.Content as BlurOverlayContent)?.BlurRadius ?? 0,
                ChromaKeyEnabled = (s.Content as IChromaKeyable)?.ChromaKeyEnabled ?? false,
                ChromaKeyColorHex = (s.Content as IChromaKeyable)?.ChromaKeyColor.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ChromaKeySimilarity = (s.Content as IChromaKeyable)?.ChromaKeySimilarity ?? 40,
                OpacityPercent = s.OpacityPercent,
                RotationDegrees = s.RotationDegrees,
                TimerMode = (s.Content as TimerOverlayContent)?.TimerMode ?? TimerMode.CountDown,
                TimerDurationSeconds = (s.Content as TimerOverlayContent)?.TimerDurationSeconds ?? 300,
                TimerAutoStartOnGoLive = (s.Content as TimerOverlayContent)?.AutoStartOnGoLive ?? false,
                TextFontFamily = (s.Content as IHasTextStyle)?.Style.FontFamily,
                TextFontSize = (s.Content as IHasTextStyle)?.Style.FontSize,
                TextFontColorHex = (s.Content as IHasTextStyle)?.Style.FontColor.ToString(System.Globalization.CultureInfo.InvariantCulture),
                TextIsBold = (s.Content as IHasTextStyle)?.Style.IsBold,
                TextIsItalic = (s.Content as IHasTextStyle)?.Style.IsItalic,
                TextAlignment = (s.Content as IHasTextStyle)?.Style.Alignment.ToString(),
                TextOutlineEnabled = (s.Content as IHasTextStyle)?.Style.OutlineEnabled,
                TextOutlineColorHex = (s.Content as IHasTextStyle)?.Style.OutlineColor.ToString(System.Globalization.CultureInfo.InvariantCulture),
                TextOutlineThickness = (s.Content as IHasTextStyle)?.Style.OutlineThickness
            }).ToList()
        }).ToList();

        if (ActiveProfile is not null && ActiveProfile.Id != "none")
        {
            // Save as override on the active profile!
            ActiveProfile.SceneSetOverrides[sceneSetId] = scenesSnapshot;
        }
        else
        {
            // Save directly to the scene set defaults!
            if (SceneEditor.ActiveSceneSet is not null)
            {
                _sceneSetService.SaveSceneSetLayout(SceneEditor.ActiveSceneSet, scenesSnapshot);
            }
            else
            {
                _localDefaultScenes = scenesSnapshot;
            }
        }

        // Save profiles and global settings configuration
        _settings.Save(BuildSettingsSnapshot());
        _settings.SaveStreamKeys(Profiles
            .Where(p => p.Id != "none" && !p.KeyRetrievableViaApi && !string.IsNullOrEmpty(p.StreamKey))
            .ToDictionary(p => p.Id, p => p.StreamKey));

        HasUnsavedChanges = false;
        BakeOverridesToSceneSetCommand.NotifyCanExecuteChanged();
    }

    private bool CanBakeOverridesToSceneSet() => SceneEditor.ActiveSceneSet is not null && ActiveProfile is not null && ActiveProfile.Id != "none" && ActiveProfile.SceneSetOverrides.ContainsKey(SceneEditor.ActiveSceneSet.Id);

    [RelayCommand(CanExecute = nameof(CanBakeOverridesToSceneSet))]
    private async Task BakeOverridesToSceneSetAsync()
    {
        if (SceneEditor.ActiveSceneSet is null || ActiveProfile is null) return;

        var confirm = await _dialogs.ConfirmAsync(
            "Bake Overrides to Set Defaults",
            $"This will write the current layout overrides directly into the Scene Set default layout files for '{SceneEditor.ActiveSceneSet.Name}', and clear this profile's overrides for it. Are you sure you want to proceed?",
            "Bake Defaults",
            "Cancel");

        if (!confirm) return;

        var scenesSnapshot = SceneEditor.Scenes.Select(scene => new SceneSettings
        {
            Id = scene.Id,
            Name = scene.Name,
            CanvasResolutionWidth = scene.CanvasResolutionWidth,
            CanvasResolutionHeight = scene.CanvasResolutionHeight,
            SwitchHotkeyKey = scene.SwitchHotkey?.Key.ToString(),
            SwitchHotkeyModifiers = scene.SwitchHotkey?.Modifiers.ToString(),
            Slots = scene.Slots.Select(s => new SlotSettings
            {
                IsPrimary = s.IsPrimary,
                IsOverlay = s.IsOverlay,
                OverlayKind = s.OverlayKind,
                SourceId = s.SourceId,
                DisplayName = s.DisplayName,
                ImagePath = (s.Content as ImageOverlayContent)?.ImagePath,
                OverlayText = (s.Content as TextOverlayContent)?.OverlayText,
                OverlayColorHex = (s.Content as ColorOverlayContent)?.OverlayColor?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                VideoPath = (s.Content as VideoOverlayContent)?.VideoPath,
                XPercent = s.XPercent,
                YPercent = s.YPercent,
                WPercent = s.WPercent,
                HPercent = s.HPercent,
                CornerRadiusPercent = s.CornerRadiusPercent,
                BlurRadius = (s.Content as BlurOverlayContent)?.BlurRadius ?? 0,
                ChromaKeyEnabled = (s.Content as IChromaKeyable)?.ChromaKeyEnabled ?? false,
                ChromaKeyColorHex = (s.Content as IChromaKeyable)?.ChromaKeyColor.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ChromaKeySimilarity = (s.Content as IChromaKeyable)?.ChromaKeySimilarity ?? 40,
                OpacityPercent = s.OpacityPercent,
                RotationDegrees = s.RotationDegrees,
                TimerMode = (s.Content as TimerOverlayContent)?.TimerMode ?? TimerMode.CountDown,
                TimerDurationSeconds = (s.Content as TimerOverlayContent)?.TimerDurationSeconds ?? 300,
                TimerAutoStartOnGoLive = (s.Content as TimerOverlayContent)?.AutoStartOnGoLive ?? false,
                TextFontFamily = (s.Content as IHasTextStyle)?.Style.FontFamily,
                TextFontSize = (s.Content as IHasTextStyle)?.Style.FontSize,
                TextFontColorHex = (s.Content as IHasTextStyle)?.Style.FontColor.ToString(System.Globalization.CultureInfo.InvariantCulture),
                TextIsBold = (s.Content as IHasTextStyle)?.Style.IsBold,
                TextIsItalic = (s.Content as IHasTextStyle)?.Style.IsItalic,
                TextAlignment = (s.Content as IHasTextStyle)?.Style.Alignment.ToString(),
                TextOutlineEnabled = (s.Content as IHasTextStyle)?.Style.OutlineEnabled,
                TextOutlineColorHex = (s.Content as IHasTextStyle)?.Style.OutlineColor.ToString(System.Globalization.CultureInfo.InvariantCulture),
                TextOutlineThickness = (s.Content as IHasTextStyle)?.Style.OutlineThickness
            }).ToList()
        }).ToList();

        // Write directly to the Scene Set defaults
        _sceneSetService.SaveSceneSetLayout(SceneEditor.ActiveSceneSet, scenesSnapshot);

        // Clear the overrides on the active profile
        ActiveProfile.SceneSetOverrides.Remove(SceneEditor.ActiveSceneSet.Id);

        // Save settings to record the removed override
        _settings.Save(BuildSettingsSnapshot());

        HasUnsavedChanges = false;
        BakeOverridesToSceneSetCommand.NotifyCanExecuteChanged();
        await _dialogs.InfoAsync("Bake Overrides", "Current overrides successfully baked into the Scene Set default files!");
    }

    private async Task RestoreSessionAsync()
    {
        await RestoreTwitchSessionAsync();
        await RestoreYouTubeSessionAsync();
    }

    private async Task RestoreTwitchSessionAsync()
    {
        var result = await _twitchAuth.TryRestoreAsync();
        if (result is not null)
        {
            foreach (var p in Profiles.Where(p => p.ServiceKind == StreamServiceKind.Twitch))
            {
                p.ConnectedAccountLabel = result.Username;
                p.ConnectedUserId = result.UserId;
                await ApplyTwitchStreamKeyAsync(p, result.AccessToken, result.UserId);
            }
            if (ActiveProfile?.ServiceKind == StreamServiceKind.Twitch)
            {
                _ = FetchAndApplyChannelMetadataAsync();
            }
        }
    }

    private async Task RestoreYouTubeSessionAsync()
    {
        var result = await _youTubeAuth.TryRestoreAsync();
        if (result is null) return;

        foreach (var p in Profiles.Where(p => p.ServiceKind == StreamServiceKind.YouTube))
        {
            p.ConnectedAccountLabel = result.ChannelName;
            await ApplyYouTubeStreamKeyAsync(p, result.AccessToken);
        }
        if (ActiveProfile?.ServiceKind == StreamServiceKind.YouTube)
        {
            _ = FetchAndApplyChannelMetadataAsync();
        }
    }

    /// <summary>Re-derives a profile's connection status (and, for YouTube, its stream key)
    /// from the actual OAuth session for whatever service it currently targets — used after a
    /// profile's ServiceKind changes, so the UI reflects the real state for the new service
    /// immediately instead of only after a manual disconnect/reconnect.</summary>
    private async Task RefreshProfileConnectionAsync(StreamingProfile profile)
    {
        switch (profile.ServiceKind)
        {
            case StreamServiceKind.Twitch:
                var twitchResult = await _twitchAuth.TryRestoreAsync();
                profile.ConnectedAccountLabel = twitchResult?.Username;
                profile.ConnectedUserId = twitchResult?.UserId;
                if (twitchResult is not null)
                {
                    await ApplyTwitchStreamKeyAsync(profile, twitchResult.AccessToken, twitchResult.UserId);
                    if (ReferenceEquals(profile, ActiveProfile))
                        _ = FetchAndApplyChannelMetadataAsync();
                }
                break;
            case StreamServiceKind.YouTube:
                var youTubeResult = await _youTubeAuth.TryRestoreAsync();
                profile.ConnectedAccountLabel = youTubeResult?.ChannelName;
                if (youTubeResult is not null)
                {
                    await ApplyYouTubeStreamKeyAsync(profile, youTubeResult.AccessToken);
                    if (ReferenceEquals(profile, ActiveProfile))
                        _ = FetchAndApplyChannelMetadataAsync();
                }
                break;
            default:
                profile.ConnectedAccountLabel = null;
                break;
        }
    }

    private async Task ApplyYouTubeStreamKeyAsync(StreamingProfile profile, string accessToken)
    {
        var key = await _youTubeAuth.TryFetchStreamKeyAsync(accessToken);
        if (key is not null)
        {
            profile.ServerUrl = key.IngestionAddress;
            profile.StreamKey = key.StreamName;
        }
    }

    private async Task ApplyTwitchStreamKeyAsync(StreamingProfile profile, string accessToken, string userId)
    {
        var key = await _twitchAuth.TryFetchStreamKeyAsync(accessToken, userId);
        if (key is not null)
        {
            profile.ServerUrl = "rtmp://live.twitch.tv/app";
            profile.StreamKey = key;
        }
    }

    private async Task FetchAndApplyChannelMetadataAsync()
    {
        if (ActiveProfile is null || !ActiveProfile.IsConnected) return;

        if (ActiveProfile.ServiceKind == StreamServiceKind.Twitch)
        {
            var token = _twitchAuth.GetAccessToken();
            var userId = ActiveProfile.ConnectedUserId;
            if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(userId))
            {
                var info = await _twitchAuth.TryFetchChannelInfoAsync(token, userId);
                if (info is not null)
                {
                    StreamTitle = info.Value.Title;
                    StreamCategory = info.Value.GameName;
                }
            }
        }
        else if (ActiveProfile.ServiceKind == StreamServiceKind.YouTube)
        {
            var token = await _youTubeAuth.GetAccessTokenAsync();
            if (!string.IsNullOrEmpty(token))
            {
                var info = await _youTubeAuth.TryFetchBroadcastInfoAsync(token);
                if (info is not null)
                {
                    StreamTitle = info.Value.Title;
                    StreamCategory = info.Value.Description;
                }
            }
        }
    }

    private async Task PromptAndLoadSceneSetAsync(string? newSetId)
    {
        var result = await _dialogs.PromptUnsavedChangesAsync(
            "Unsaved Changes",
            "You have unsaved changes in your layout. Would you like to save them before switching layouts?");

        if (result == "cancel")
        {
            if (ActiveProfile is not null)
            {
                ActiveProfile.PropertyChanged -= OnProfilePropertyChanged;
                ActiveProfile.LinkedSceneSetId = _lastLoadedSceneSetId;
                ActiveProfile.PropertyChanged += OnProfilePropertyChanged;
            }
            return;
        }

        if (result == "save")
        {
            SaveSceneSet();
        }

        HasUnsavedChanges = false;
        await LoadSceneSetByIdAsync(newSetId);
        _lastLoadedSceneSetId = newSetId;
    }

    private void OnProfilePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StreamingProfile.Name) or nameof(StreamingProfile.ServiceKind)
            or nameof(StreamingProfile.ServerUrl) or nameof(StreamingProfile.StreamKey)
            or nameof(StreamingProfile.BitrateKbps) or nameof(StreamingProfile.Fps)
            or nameof(StreamingProfile.Encoder) or nameof(StreamingProfile.LinkedSceneSetId))
        {
            if (ReferenceEquals(ActiveProfile, sender))
            {
                if (e.PropertyName == nameof(StreamingProfile.LinkedSceneSetId))
                {
                    if (HasUnsavedChanges)
                    {
                        _ = PromptAndLoadSceneSetAsync(ActiveProfile.LinkedSceneSetId);
                    }
                    else
                    {
                        _ = LoadSceneSetByIdAsync(ActiveProfile.LinkedSceneSetId);
                        _lastLoadedSceneSetId = ActiveProfile.LinkedSceneSetId;
                    }
                }
                else if (e.PropertyName == nameof(StreamingProfile.ServiceKind))
                {
                    UpdateChatConnection();
                    OnPropertyChanged(nameof(IsTestModeSupported));
                    OnPropertyChanged(nameof(IsTestModeSelectorVisible));
                    if (!IsTestModeSupported) IsTestModeEnabled = false;
                    // StreamingProfile.OnServiceKindChanged already cleared the stale key/label
                    // synchronously — this re-derives the real ones for whichever service the
                    // profile now targets, from the actual OAuth session (and, for YouTube,
                    // re-fetches the current stream key) rather than requiring a manual
                    // disconnect/reconnect to notice the switch.
                    _ = RefreshProfileConnectionAsync((StreamingProfile)sender!);
                }
                else if (e.PropertyName == nameof(StreamingProfile.BitrateKbps))
                {
                    _bitrateKbps = ActiveProfile.BitrateKbps;
                    OnPropertyChanged(nameof(BitrateKbps));
                }
                else if (e.PropertyName == nameof(StreamingProfile.Fps))
                {
                    _fps = ActiveProfile.Fps;
                    OnPropertyChanged(nameof(Fps));
                }
                else if (e.PropertyName == nameof(StreamingProfile.Encoder))
                {
                    _encoder = ActiveProfile.Encoder ?? "libx264";
                    OnPropertyChanged(nameof(Encoder));
                }
            }

            RefreshStartStreamAvailability();
            ScheduleSaveSettings();
        }
    }

    [RelayCommand]
    private async Task ConnectServiceAsync(StreamingProfile profile)
    {
        switch (profile.ServiceKind)
        {
            case StreamServiceKind.Twitch:
                await ConnectTwitchAsync(profile);
                break;
            case StreamServiceKind.YouTube:
                await ConnectYouTubeAsync(profile);
                break;
        }
        UpdateChatConnection();
        _ = FetchAndApplyChannelMetadataAsync();
    }

    private async Task ConnectTwitchAsync(StreamingProfile profile)
    {
        if (profile.IsConnected)
        {
            _twitchAuth.Disconnect();
            foreach (var p in Profiles.Where(p => p.ServiceKind == StreamServiceKind.Twitch))
            {
                p.ConnectedAccountLabel = null;
                p.ConnectedUserId = null;
            }
            return;
        }

        var result = await _twitchAuth.ConnectAsync();
        if (result is not null)
        {
            foreach (var p in Profiles.Where(p => p.ServiceKind == StreamServiceKind.Twitch))
            {
                p.ConnectedAccountLabel = result.Username;
                p.ConnectedUserId = result.UserId;
                await ApplyTwitchStreamKeyAsync(p, result.AccessToken, result.UserId);
            }
        }
        else
        {
            await _dialogs.WarningAsync("Connect Twitch", "Twitch sign-in didn't complete. Please try again.");
        }
    }

    private async Task ConnectYouTubeAsync(StreamingProfile profile)
    {
        if (profile.IsConnected)
        {
            _youTubeAuth.Disconnect();
            foreach (var p in Profiles.Where(p => p.ServiceKind == StreamServiceKind.YouTube))
            {
                p.ConnectedAccountLabel = null;
            }
            return;
        }

        var result = await _youTubeAuth.ConnectAsync();
        if (result is null)
        {
            await _dialogs.WarningAsync("Connect YouTube", "YouTube sign-in didn't complete. Please try again.");
            return;
        }

        foreach (var p in Profiles.Where(p => p.ServiceKind == StreamServiceKind.YouTube))
        {
            p.ConnectedAccountLabel = result.ChannelName;
            await ApplyYouTubeStreamKeyAsync(p, result.AccessToken);
        }
    }

    [RelayCommand]
    private void AddProfile()
    {
        var customCount = Profiles.Count(p => p.Id != "none");
        var name = $"Profile {customCount + 1}";

        var profile = new StreamingProfile(Guid.NewGuid().ToString("N"), name)
        {
            ServiceKind = ActiveProfile?.ServiceKind ?? StreamServiceKind.Twitch,
            ServerUrl = ActiveProfile?.ServerUrl ?? "rtmp://live.twitch.tv/app",
            StreamKey = ActiveProfile?.StreamKey ?? "",
            BitrateKbps = ActiveProfile?.BitrateKbps ?? 6000,
            Fps = ActiveProfile?.Fps ?? 30,
            Encoder = ActiveProfile?.Encoder ?? Encoder
        };
        profile.PropertyChanged += OnProfilePropertyChanged;
        Profiles.Add(profile);
        ActiveProfile = profile;
        ScheduleSaveSettings();
    }

    private bool CanRemoveProfile(StreamingProfile? profile) => profile is not null && profile.Id != "none";

    [RelayCommand(CanExecute = nameof(CanRemoveProfile))]
    private void RemoveProfile(StreamingProfile profile)
    {
        if (profile.Id == "none") return;

        profile.PropertyChanged -= OnProfilePropertyChanged;
        var index = Profiles.IndexOf(profile);
        Profiles.Remove(profile);

        if (ReferenceEquals(ActiveProfile, profile))
        {
            ActiveProfile = Profiles[Math.Max(0, index - 1)];
        }
        ScheduleSaveSettings();
    }

    [RelayCommand]
    private async Task ImportSceneSetAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Scene Set",
            Filter = "Scene Set Files|*.sfset|All Files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var reg = _sceneSetService.ImportSceneSet(dialog.FileName);
            SceneEditor.RegisteredSceneSets.Add(reg);
            ScheduleSaveSettings();
            await _dialogs.WarningAsync("Import Scene Set", $"Successfully imported Scene Set '{reg.Name}'!");
        }
        catch (Exception ex)
        {
            await _dialogs.WarningAsync("Import Scene Set", $"Failed to import Scene Set: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ExportActiveSceneSetAsync()
    {
        if (SceneEditor.ActiveSceneSet is null)
        {
            await _dialogs.WarningAsync("Export Scene Set", "No active Scene Set is loaded. You can only export custom layouts that are registered as Scene Sets.");
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Scene Set",
            Filter = "Scene Set Files|*.sfset",
            FileName = $"{SceneEditor.ActiveSceneSet.Name}.sfset"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var scenesSnapshot = SceneEditor.Scenes.Select(sc => new SceneSettings
            {
                Id = sc.Id,
                Name = sc.Name,
                CanvasResolutionWidth = sc.CanvasResolutionWidth,
                CanvasResolutionHeight = sc.CanvasResolutionHeight,
                Slots = sc.Slots.Select(s => new SlotSettings
                {
                    IsPrimary = s.IsPrimary,
                    IsOverlay = s.IsOverlay,
                    OverlayKind = s.OverlayKind,
                    SourceId = s.SourceId,
                    DisplayName = s.DisplayName,
                    ImagePath = (s.Content as ImageOverlayContent)?.ImagePath,
                    OverlayText = (s.Content as TextOverlayContent)?.OverlayText,
                    OverlayColorHex = (s.Content as ColorOverlayContent)?.OverlayColor?.ToString(CultureInfo.InvariantCulture),
                    VideoPath = (s.Content as VideoOverlayContent)?.VideoPath,
                    XPercent = s.XPercent,
                    YPercent = s.YPercent,
                    WPercent = s.WPercent,
                    HPercent = s.HPercent,
                    CornerRadiusPercent = s.CornerRadiusPercent,
                    BlurRadius = (s.Content as BlurOverlayContent)?.BlurRadius ?? 0,
                    ChromaKeyEnabled = (s.Content as IChromaKeyable)?.ChromaKeyEnabled ?? false,
                    ChromaKeyColorHex = (s.Content as IChromaKeyable)?.ChromaKeyColor.ToString(CultureInfo.InvariantCulture),
                    ChromaKeySimilarity = (s.Content as IChromaKeyable)?.ChromaKeySimilarity ?? 40,
                    OpacityPercent = s.OpacityPercent,
                    RotationDegrees = s.RotationDegrees,
                    TimerMode = (s.Content as TimerOverlayContent)?.TimerMode ?? TimerMode.CountDown,
                    TimerDurationSeconds = (s.Content as TimerOverlayContent)?.TimerDurationSeconds ?? 300,
                    TimerAutoStartOnGoLive = (s.Content as TimerOverlayContent)?.AutoStartOnGoLive ?? false,
                    TextFontFamily = (s.Content as IHasTextStyle)?.Style.FontFamily,
                    TextFontSize = (s.Content as IHasTextStyle)?.Style.FontSize,
                    TextFontColorHex = (s.Content as IHasTextStyle)?.Style.FontColor.ToString(CultureInfo.InvariantCulture),
                    TextIsBold = (s.Content as IHasTextStyle)?.Style.IsBold,
                    TextIsItalic = (s.Content as IHasTextStyle)?.Style.IsItalic,
                    TextAlignment = (s.Content as IHasTextStyle)?.Style.Alignment.ToString(),
                    TextOutlineEnabled = (s.Content as IHasTextStyle)?.Style.OutlineEnabled,
                    TextOutlineColorHex = (s.Content as IHasTextStyle)?.Style.OutlineColor.ToString(CultureInfo.InvariantCulture),
                    TextOutlineThickness = (s.Content as IHasTextStyle)?.Style.OutlineThickness
                }).ToList()
            }).ToList();

            _sceneSetService.ExportSceneSet(dialog.FileName, SceneEditor.ActiveSceneSet.Name, SceneEditor.ActiveSceneSet.Author, scenesSnapshot);
            await _dialogs.WarningAsync("Export Scene Set", "Successfully exported Scene Set!");
        }
        catch (Exception ex)
        {
            await _dialogs.WarningAsync("Export Scene Set", $"Failed to export Scene Set: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task UninstallSceneSetAsync(SceneSetRegistration reg)
    {
        if (ActiveProfile is not null && ActiveProfile.LinkedSceneSetId == reg.Id)
        {
            await _dialogs.WarningAsync("Uninstall Scene Set", "Cannot uninstall this Scene Set because it is currently linked to the active Streaming Profile.");
            return;
        }

        SceneEditor.UninstallSceneSet(reg);

        foreach (var profile in Profiles.Where(p => p.LinkedSceneSetId == reg.Id))
        {
            profile.LinkedSceneSetId = null;
        }

        ScheduleSaveSettings();
        await _dialogs.WarningAsync("Uninstall Scene Set", $"Successfully uninstalled '{reg.Name}'!");
    }

    private async Task LoadSceneSetByIdAsync(string? sceneSetId)
    {
        SceneSetRegistration? reg = null;
        if (!string.IsNullOrEmpty(sceneSetId))
        {
            reg = SceneEditor.RegisteredSceneSets.FirstOrDefault(r => r.Id == sceneSetId);
        }

        await LoadSceneSetAsync(reg);
    }

    private async Task LoadSceneSetAsync(SceneSetRegistration? reg)
    {
        if (SceneEditor.ActiveScene is not null)
            await SceneEditor.DeactivateSceneAsync(SceneEditor.ActiveScene);

        SceneEditor.ActiveSceneSet = reg;
        SceneEditor.SetSceneSetMetadataFromRegistration(reg);

        List<SceneSettings> loadedSettings;
        var sceneSetId = reg?.Id ?? "default";
        if (ActiveProfile is not null && ActiveProfile.Id != "none" && ActiveProfile.SceneSetOverrides.TryGetValue(sceneSetId, out var overrideScenes))
        {
            loadedSettings = overrideScenes;
        }
        else if (reg is not null)
        {
            loadedSettings = _sceneSetService.LoadSceneSetLayout(reg);
        }
        else
        {
            loadedSettings = _localDefaultScenes;
        }

        SceneEditor.Scenes.Clear();
        foreach (var savedScene in loadedSettings)
        {
            SceneEditor.Scenes.Add(SceneEditor.BuildSceneFromSettings(savedScene));
        }

        if (SceneEditor.Scenes.Count == 0)
        {
            SceneEditor.Scenes.Add(SceneEditor.CreateBlankScene("Scene 1"));
        }

        SceneEditor.ActiveScene = SceneEditor.Scenes[0];
        HasUnsavedChanges = false;
        _isInitializing = false;
        _lastLoadedSceneSetId = reg?.Id;
        BakeOverridesToSceneSetCommand.NotifyCanExecuteChanged();
        RefreshStartStreamAvailability();
    }

    private bool CanStartStream()
    {
        if (IsStreaming) return false;
        
        var baseValid = SceneEditor.Slots.Count > 0 && SceneEditor.Slots.All(s => s.IsOverlay || !string.IsNullOrEmpty(s.SourceId));
        if (!baseValid) return false;

        if (IsRecordOnlyMode)
        {
            return !string.IsNullOrWhiteSpace(RecordFolderPath);
        }
        else
        {
            return ActiveProfile is not null &&
                   !string.IsNullOrWhiteSpace(ActiveProfile.ServerUrl) &&
                   !string.IsNullOrWhiteSpace(ActiveProfile.StreamKey);
        }
    }

    /// <summary>Human-readable reason Go Live/Test Stream are disabled — null once
    /// <see cref="CanStartStream"/> is satisfied. Bound to both buttons' ToolTips so "why won't
    /// this let me click it" is visible in the UI instead of requiring a debugger.</summary>
    public string? StartStreamBlockedReason
    {
        get
        {
            if (IsStreaming) return null;

            var reasons = new List<string>();
            var unassigned = SceneEditor.Slots.Count(s => !s.IsOverlay && string.IsNullOrEmpty(s.SourceId));
            if (SceneEditor.Slots.Count == 0)
                reasons.Add("the active scene has no sources");
            else if (unassigned > 0)
                reasons.Add($"{unassigned} source slot(s) have no source selected");

            if (IsRecordOnlyMode)
            {
                if (string.IsNullOrWhiteSpace(RecordFolderPath))
                    reasons.Add("no recording folder has been selected");
            }
            else
            {
                if (ActiveProfile is null)
                    reasons.Add("no streaming profile is selected");
                else
                {
                    if (string.IsNullOrWhiteSpace(ActiveProfile.ServerUrl)) reasons.Add("the profile's Server URL is empty");
                    if (string.IsNullOrWhiteSpace(ActiveProfile.StreamKey)) reasons.Add("the profile's Stream Key is empty");
                }
            }

            return reasons.Count == 0 ? null : (IsRecordOnlyMode ? "Can't start recording: " : "Can't start streaming: ") + string.Join("; ", reasons) + ".";
        }
    }

    /// <summary>Single choke point for re-checking Go Live availability — call this (not
    /// StartStreamCommand.NotifyCanExecuteChanged directly) from anywhere a condition in
    /// CanStartStream could have changed, so StartStreamBlockedReason always stays in sync with
    /// the button's actual enabled state.</summary>
    private void RefreshStartStreamAvailability()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() =>
            {
                StartStreamCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(StartStreamBlockedReason));
            }));
        }
        else
        {
            StartStreamCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(StartStreamBlockedReason));
        }
    }

    /// <summary>Whether this goes out as a real stream or a bandwidth test is decided by the
    /// "Test Mode" toggle (<see cref="IsTestModeEnabled"/>) rather than a separate button/command
    /// — see StartStreamCoreAsync's testMode param for what that actually changes.</summary>
    [RelayCommand(CanExecute = nameof(CanStartStream))]
    private Task StartStreamAsync() => StartStreamCoreAsync(testMode: IsTestModeEnabled, recordOnly: IsRecordOnlyMode);

    private async Task StartStreamCoreAsync(bool testMode, bool recordOnly = false)
    {
        // Capture sources are started as soon as they're picked (UpdateSlotCaptureAsync), so
        // the active scene's live preview keeps running whether or not you're actually
        // streaming — this only starts one if that somehow hasn't happened yet (e.g. a
        // settings-restore edge case), rather than tearing down an already-active session.
        if (SceneEditor.ActiveScene is not null)
            await SceneEditor.StartAllSlotCapturesAsync(SceneEditor.ActiveScene);

        var sources = SceneEditor.BuildStreamSources();

        // The compositor (which both the preview and the stream encoder read from) only
        // knows the current primary/PiP layout once Config is sent.
        await _core.SendCommandAsync(SceneEditor.BuildConfigCommand());

        string? rtmpUrl = null;
        if (!recordOnly)
        {
            if (ActiveProfile is null) return;

            // Twitch's test mode is just a URL variant (BuildRtmpUrl appends the query param) —
            // YouTube has no such flag, so it needs an actual ephemeral unlisted broadcast to
            // publish into instead (see IsTestModeSupported's doc comment for why these can't share
            // one code path).
            if (testMode && ActiveProfile.ServiceKind == StreamServiceKind.YouTube)
            {
                var accessToken = await _youTubeAuth.GetAccessTokenAsync();
                var testBroadcast = accessToken is null ? null : await _youTubeAuth.CreateTestBroadcastAsync(accessToken);
                if (testBroadcast is null)
                {
                    ErrorMessage = "Couldn't create a YouTube test broadcast — check that YouTube is connected and try again.";
                    ScheduleErrorDismiss();
                    return;
                }

                _activeYouTubeTestBroadcastId = testBroadcast.BroadcastId;
                rtmpUrl = $"{testBroadcast.IngestionAddress.TrimEnd('/')}/{testBroadcast.StreamName}";
            }
            else
            {
                rtmpUrl = ActiveProfile.BuildRtmpUrl(testMode);
            }
        }

        // Whatever's checked in the Audio Sources panel (see AudioSources/RebuildAudioSources),
        // with each device's channel-strip gain/mute/solo — previously hardcoded to [] (no
        // audio device selected at all), which is very likely why YouTube specifically was
        // showing "no data": it's markedly stricter than Twitch about ever surfacing a
        // video-only RTMP publish as a live stream, even though the publish itself completes
        // without any protocol-level error on our end.
        // Voicemeeter's virtual "Input" (render) devices never carry real audio through WASAPI
        // loopback — confirmed empirically: Voicemeeter's driver keeps a synthetic buffer
        // stream alive for WASAPI's benefit, but the actual mixed audio never reaches it (it
        // flows through Voicemeeter's own internal engine instead). Selecting one still works
        // for local monitoring (the channel-strip meter reads Voicemeeter's own level via the
        // VoicemeeterRemote API instead — see native/crates/core/src/voicemeeter.rs), but
        // including it here would silently stream dead air, so it's excluded specifically from
        // what actually gets sent to StartStream. Voicemeeter's own capture-kind "Out" busses
        // (e.g. "Voicemeeter Out B1") aren't affected — those are regular WASAPI/CPAL capture
        // devices Voicemeeter genuinely writes mixed audio into.
        var audioSourceConfigs = AudioSources
            .Where(a => a.IsSelected && !IsVoicemeeterRenderDevice(a.Device))
            .Select(a => new AudioSourceConfig(a.Device.Id, EffectiveGain(a), a.IsMuted, a.IsSolo, IsDuckingTrigger: IsDuckingEnabled && a.Device.Id == DuckingTriggerDeviceId))
            .ToArray();

        IsTestStreaming = testMode && !recordOnly;
        await _core.SendCommandAsync(new StartStreamCommand(
            rtmpUrl, BitrateKbps, Fps,
            OutputWidth: null, OutputHeight: null, FitMode: null,
            Encoder, sources, audioSourceConfigs, RecordPath: BuildRecordPathIfEnabled(force: recordOnly)));
    }

    [RelayCommand(CanExecute = nameof(IsStreaming))]
    private Task StopStreamAsync() =>
        // Deliberately doesn't stop any capture sessions — those are tied to slot selection,
        // not streaming, so live previews (primary and PiP) keep running after you stop.
        _core.SendCommandAsync(new StopStreamCommand());

    /// <summary>Tears down the ephemeral YouTube test broadcast (if any) from the stream that
    /// just ended — called from the StreamStoppedEvent handler in GoLiveViewModel.cs, which fires
    /// regardless of *how* the stream ended, so this doesn't need its own hook in StopStreamAsync.
    /// Fire-and-forget by design: nothing in the UI is waiting on this, and it shouldn't block the
    /// rest of the stream-stopped handling on a network round-trip to YouTube.</summary>
    private void EndActiveYouTubeTestBroadcastIfAny()
    {
        if (_activeYouTubeTestBroadcastId is null) return;
        var broadcastId = _activeYouTubeTestBroadcastId;
        _activeYouTubeTestBroadcastId = null;

        _ = Task.Run(async () =>
        {
            var accessToken = await _youTubeAuth.GetAccessTokenAsync();
            if (accessToken is not null)
                await _youTubeAuth.EndTestBroadcastAsync(accessToken, broadcastId);
        });
    }

    /// <summary>True for a Voicemeeter virtual "Input" (render) device — see the comment at
    /// StartStreamAsync's audioSourceConfigs construction for why these are excluded from
    /// actual streaming despite being selectable for monitoring.</summary>
    private static bool IsVoicemeeterRenderDevice(NativeAudioDevice device) =>
        device.Kind == "output" && device.Name.Contains("voicemeeter", StringComparison.OrdinalIgnoreCase);

    partial void OnIsStreamingChanged(bool value)
    {
        RefreshStartStreamAvailability();
        StopStreamCommand.NotifyCanExecuteChanged();

        // Always re-lock on a fresh stream — unlocking is a deliberate per-session choice,
        // not a standing preference.
        IsLiveEditUnlocked = false;
        OnPropertyChanged(nameof(CanEditSlots));
    }

    partial void OnIsLiveEditUnlockedChanged(bool value) => OnPropertyChanged(nameof(CanEditSlots));
}
