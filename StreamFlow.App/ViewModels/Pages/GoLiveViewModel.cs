using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;



using Microsoft.Extensions.Logging;

using StreamFlow.App.Services;
using StreamFlow.App.Services.Core;

namespace StreamFlow.App.ViewModels.Pages;

/// <summary>Core of the Go Live page's ViewModel: DI wiring, cross-cutting settings persistence
/// (used by every concern below), and the core-bridge event dispatcher. The rest of this
/// ViewModel's members live in sibling partial-class files, split by concern:
/// GoLiveViewModel.Streaming.cs (profiles, OAuth, start/stop stream), GoLiveViewModel.Audio.cs
/// (audio source selection/metering), GoLiveViewModel.Chat.cs (chat overlay connection). Scene/
/// layer editing itself lives in the shared <see cref="SceneEditorViewModel"/> (SceneEditor),
/// not here — see its own doc comment.</summary>
public partial class GoLiveViewModel : ViewModel
{
    private readonly CoreBridgeService _core;
    private readonly IDialogService _dialogs;
    private readonly TwitchAuthService _twitchAuth;
    private readonly YouTubeAuthService _youTubeAuth;
    private readonly TwitchChatService _twitchChat;
    private readonly YouTubeChatService _youTubeChat;
    private readonly GoLiveSettingsService _settings;
    private readonly SceneSetService _sceneSetService;
    private readonly EventBus _eventBus;
    private readonly ILogger<GoLiveViewModel> _logger;

    /// <summary>Shared with ScenesViewModel — same live Scenes/Slots/ActiveScene state,
    /// registered as a DI singleton. See SceneEditorViewModel's own doc comment for the
    /// decoupling-event design this class subscribes to below.</summary>
    public SceneEditorViewModel SceneEditor { get; }

    /// <summary>Last primary resolution we saw via SourcesEvent — lets us detect an actual
    /// resolution change (not just the first-ever discovery) so static overlays can be
    /// refreshed against it. See GetImageOverlayCapSize.</summary>
    private (uint Width, uint Height)? _lastKnownPrimaryResolution;

    [ObservableProperty]
    private string _coreStateText = "Not started";

    [ObservableProperty]
    private string _lastPongText = "Never";

    /// <summary>Tail of the core's stderr/tracing output, for the Core Diagnostics panel.
    /// Temporary while investigating the startup CPU-spike bug — surfaces the [diag] capture-
    /// dimension logging added in capture.rs so it's visible without a separate log file.</summary>
    public ObservableCollection<string> CoreLogLines { get; } = [];

    /// <summary>Gates the Core Diagnostics panel's visibility in GoLiveView — a live tail of the
    /// core's internal tracing output isn't something a released build should surface to end
    /// users. Doesn't affect --diag-log itself (see CoreBridgeService.DiagLogEnabled): that still
    /// persists the full session to disk in Release builds too, in case its detail is useful for
    /// a submitted issue — this only hides the in-app live-tail UI.</summary>
#if DEBUG
    public bool IsDebugBuild => true;
#else
    public bool IsDebugBuild => false;
#endif

    private const int MaxCoreLogLines = 500;

    [RelayCommand]
    private void ClearCoreLog() => CoreLogLines.Clear();

    [RelayCommand]
    private void CopyCoreLog()
    {
        if (CoreLogLines.Count == 0) return;
        System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, CoreLogLines));
    }

    [RelayCommand]
    private async Task SaveCoreLogAsync()
    {
        if (CoreLogLines.Count == 0) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save Core Diagnostics Log",
            Filter = "Log Files|*.log|Text Files|*.txt",
            FileName = $"streamflow-core-{DateTime.Now:yyyyMMdd_HHmmss}.log"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            await File.WriteAllTextAsync(dialog.FileName, string.Join(Environment.NewLine, CoreLogLines));
        }
        catch (Exception ex)
        {
            await _dialogs.WarningAsync("Save Core Diagnostics Log", $"Failed to save log: {ex.Message}");
        }
    }

    /// <summary>Rolling measured rate of incoming preview frames — independent of
    /// <see cref="LiveFps"/> (which only reflects the encoder, while actually streaming).</summary>
    [ObservableProperty]
    private float _previewFps;

    private readonly Queue<DateTime> _recentFrameTimes = new();

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    private bool _isInitializing = true;
    private string? _lastLoadedSceneSetId;

    public GoLiveViewModel(
        CoreBridgeService core, IDialogService dialogs, TwitchAuthService twitchAuth, YouTubeAuthService youTubeAuth,
        TwitchChatService twitchChat, YouTubeChatService youTubeChat,
        GoLiveSettingsService settings, SceneSetService sceneSetService, GpuEncoderDetectionService gpuEncoderDetection,
        SceneEditorViewModel sceneEditor, EventBus eventBus, ILogger<GoLiveViewModel> logger)
    {
        _core = core;
        _dialogs = dialogs;
        _twitchAuth = twitchAuth;
        _youTubeAuth = youTubeAuth;
        _twitchChat = twitchChat;
        _youTubeChat = youTubeChat;
        _settings = settings;
        _sceneSetService = sceneSetService;
        _eventBus = eventBus;
        _logger = logger;
        SceneEditor = sceneEditor;
        _core.StateChanged += OnCoreStateChanged;
        _core.EventReceived += OnCoreEventReceived;
        _core.LogLineReceived += OnCoreLogLineReceived;
        _twitchChat.MessageReceived += OnChatMessageReceived;
        _youTubeChat.MessageReceived += OnChatMessageReceived;

        AudioSourcesView = CollectionViewSource.GetDefaultView(AudioSources);
        AudioSourcesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AudioSourceItem.GroupKey)));
        AudioSourcesView.SortDescriptions.Add(new SortDescription(nameof(AudioSourceItem.IsSelected), ListSortDirection.Descending));
        AudioSourcesView.SortDescriptions.Add(new SortDescription("Device.Kind", ListSortDirection.Ascending));
        // Toggling a device's checkbox changes which group it belongs to (see GroupKey) — live
        // shaping re-runs the group/sort evaluation on IsSelected's own PropertyChanged rather
        // than requiring a full collection replace/Refresh() to move it.
        if (AudioSourcesView is ListCollectionView audioSourcesLiveView)
        {
            audioSourcesLiveView.IsLiveGrouping = true;
            audioSourcesLiveView.LiveGroupingProperties.Add(nameof(AudioSourceItem.IsSelected));
            audioSourcesLiveView.IsLiveSorting = true;
            audioSourcesLiveView.LiveSortingProperties.Add(nameof(AudioSourceItem.IsSelected));
        }

        DesignTimeAudioSourcesView = CollectionViewSource.GetDefaultView(DesignTimeAudioSources);
        DesignTimeAudioSourcesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AudioSourceItem.GroupKey)));

        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += SceneEditor.OnDisplaySettingsChanged;

        SceneEditor.Changed += ScheduleSaveSettings;
        SceneEditor.SlotAvailabilityChanged += RefreshStartStreamAvailability;
        SceneEditor.ChatOverlayStateChanged += UpdateChatConnection;

        var saved = _settings.Load();
        _encoder = saved.Encoder ?? gpuEncoderDetection.DetectBestEncoder();
        ApplySettings(saved);

        UpdateCoreStateText();

        InitializeTimerOverlayTicker();

        _ = RestoreSessionAsync();
    }

    private void ApplySettings(GoLiveSettings saved)
    {
        _bitrateKbps = saved.BitrateKbps;
        _fps = saved.Fps;
        if (saved.Encoder is not null) _encoder = saved.Encoder;
        _masterVolumePercent = saved.MasterVolumePercent;
        _persistedSelectedAudioDeviceIds = saved.SelectedAudioDeviceIds;
        _persistedAudioDeviceDisplayNames = saved.AudioDeviceDisplayNames;

        _isSpoutOutputEnabled = true; // Always enable Spout2
        _spoutSenderName = string.IsNullOrEmpty(saved.SpoutSenderName) ? "StreamFlow" : saved.SpoutSenderName;

        _isRecordingEnabled = saved.IsRecordingEnabled;
        _recordFolderPath = string.IsNullOrEmpty(saved.RecordFolderPath)
            ? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyVideos), "StreamFlow")
            : saved.RecordFolderPath;

        // Force spout output to active
        _ = _core.SendCommandAsync(new SetSpoutOutputCommand(true, _spoutSenderName));

        SceneEditor.TransitionKind = SceneEditorViewModel.TransitionKindFromWire(saved.TransitionKind);
        SceneEditor.TransitionDurationMs = (int)saved.TransitionDurationMs;

        SceneEditor.RegisteredSceneSets.Clear();
        foreach (var reg in saved.RegisteredSceneSets)
        {
            SceneEditor.RegisteredSceneSets.Add(reg);
        }

        Profiles.Clear();
        _noneProfile.PropertyChanged -= OnProfilePropertyChanged;
        _noneProfile.PropertyChanged += OnProfilePropertyChanged;
        Profiles.Add(_noneProfile);

        foreach (var pSaved in saved.Profiles)
        {
            if (pSaved.Id == "none") continue;
            var p = new StreamingProfile(pSaved.Id, pSaved.Name)
            {
                ServiceKind = pSaved.ServiceKind,
                ServerUrl = pSaved.ServerUrl,
                BitrateKbps = pSaved.BitrateKbps,
                Fps = pSaved.Fps,
                Encoder = pSaved.Encoder ?? "libx264",
                LinkedSceneSetId = pSaved.LinkedSceneSetId,
                SceneSetOverrides = pSaved.SceneSetOverrides ?? []
            };
            p.PropertyChanged += OnProfilePropertyChanged;
            Profiles.Add(p);
        }

        var streamKeys = _settings.LoadStreamKeys();
        foreach (var p in Profiles)
        {
            if (p.Id != "none" && streamKeys.TryGetValue(p.Id, out var key))
            {
                p.StreamKey = key;
            }
        }

        if (Profiles.Count == 1)
        {
            var defaultProfile = new StreamingProfile(Guid.NewGuid().ToString("N"), "Twitch Default")
            {
                ServiceKind = StreamServiceKind.Twitch,
                ServerUrl = "rtmp://live.twitch.tv/app",
                BitrateKbps = 6000,
                Fps = 30,
                Encoder = _encoder
            };
            defaultProfile.PropertyChanged += OnProfilePropertyChanged;
            Profiles.Add(defaultProfile);
        }

        var activeId = saved.ActiveProfileId ?? saved.DefaultProfileId;
        ActiveProfile = Profiles.FirstOrDefault(p => p.Id == activeId) ?? Profiles[0];

        _localDefaultScenes = saved.Scenes;
        if (_localDefaultScenes.Count == 0)
        {
            _localDefaultScenes.Add(new SceneSettings { Name = "Scene 1" });
        }

        _ = LoadSceneSetByIdAsync(ActiveProfile?.LinkedSceneSetId);
    }

    private GoLiveSettings BuildSettingsSnapshot()
    {
        // Stream Deck server settings (IsStreamDeckServerEnabled/Port/ApiKey) are deliberately
        // owned by SettingsViewModel/StreamDeckServerService, not this ViewModel — but this method
        // builds a brand-new GoLiveSettings from scratch rather than load-modify-save, so without
        // reading the currently-persisted values forward here, every debounced autosave this
        // ViewModel triggers (any scene/profile edit — see ScheduleSaveSettings) would silently
        // reset those three fields back to their defaults, wiping whatever the Settings page had
        // just saved. Read once per snapshot rather than cached, so it always reflects the latest
        // value regardless of which page wrote it last.
        var currentOnDisk = _settings.Load();

        return new()
        {
            TransitionKind = SceneEditorViewModel.TransitionKindToWire(SceneEditor.TransitionKind),
            TransitionDurationMs = (uint)Math.Max(0, SceneEditor.TransitionDurationMs),
            Scenes = SceneEditor.ActiveSceneSet is null ? SceneEditor.Scenes.Select(scene => new SceneSettings
            {
                Id = scene.Id,
                Name = scene.Name,
                CanvasResolutionWidth = scene.CanvasResolutionWidth,
                CanvasResolutionHeight = scene.CanvasResolutionHeight,
                Slots = scene.Slots.Select(s => new SlotSettings
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
                }).ToList(),
            }).ToList() : _localDefaultScenes,
            DefaultSceneId = SceneEditor.ActiveSceneSet is null ? SceneEditor.DefaultScene?.Id : null,
            Profiles = Profiles.Where(p => p.Id != "none").Select(p => new StreamingProfileSettings
            {
                Id = p.Id,
                Name = p.Name,
                ServiceKind = p.ServiceKind,
                ServerUrl = p.ServerUrl,
                BitrateKbps = p.BitrateKbps,
                Fps = p.Fps,
                Encoder = p.Encoder,
                LinkedSceneSetId = p.LinkedSceneSetId,
                SceneSetOverrides = p.SceneSetOverrides
            }).ToList(),
            ActiveProfileId = ActiveProfile?.Id == "none" ? null : ActiveProfile?.Id,
            DefaultProfileId = ActiveProfile?.Id == "none" ? null : ActiveProfile?.Id, // Using active profile as default launch profile
            RegisteredSceneSets = SceneEditor.RegisteredSceneSets.ToList(),
            // Only overwrite with a concrete (possibly empty) list once AudioSources has
            // actually been populated this session — otherwise a save triggered before the
            // first AudioDevicesEvent arrives would wipe out a real prior selection.
            SelectedAudioDeviceIds = AudioSources.Count > 0
                ? AudioSources.Where(a => a.IsSelected).Select(a => a.Device.Id).ToList()
                : _persistedSelectedAudioDeviceIds,
            AudioDeviceDisplayNames = AudioSources.Count > 0
                ? AudioSources.Where(a => a.IsRenamed).ToDictionary(a => a.Device.Id, a => a.DisplayName)
                : _persistedAudioDeviceDisplayNames ?? [],
            MasterVolumePercent = MasterVolumePercent,
            IsSpoutOutputEnabled = IsSpoutOutputEnabled,
            SpoutSenderName = SpoutSenderName,
            IsRecordingEnabled = IsRecordingEnabled,
            RecordFolderPath = RecordFolderPath,
            BitrateKbps = BitrateKbps,
            Fps = Fps,
            IsStreamDeckServerEnabled = currentOnDisk.IsStreamDeckServerEnabled,
            StreamDeckServerPort = currentOnDisk.StreamDeckServerPort,
            StreamDeckApiKey = currentOnDisk.StreamDeckApiKey
        };
    }

    private CancellationTokenSource? _saveDebounceCts;

    /// <summary>Debounced write-through to golive_settings.json/stream_keys.dat — every profile
    /// edit (ServerUrl, StreamKey, bitrate, etc.) and scene change routes through here via
    /// OnProfilePropertyChanged/SceneEditor.Changed, but until now this only ever flipped
    /// HasUnsavedChanges without ever actually writing to disk: the only real save path was the
    /// explicit "Save Scene Set" / "Bake Overrides" buttons, so anything typed outside of a scene
    /// set edit (a streaming profile's server URL/key, say) was silently lost on restart.
    /// Deliberately leaves HasUnsavedChanges alone — that flag also gates the Scene Set-specific
    /// save prompt in PromptAndSwitchProfileAsync/PromptAndLoadSceneSetAsync, which this debounce
    /// shouldn't suppress.</summary>
    private void ScheduleSaveSettings()
    {
        if (_isInitializing) return;
        HasUnsavedChanges = true;

        _saveDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _saveDebounceCts = cts;

        _ = Task.Delay(TimeSpan.FromSeconds(1), cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                _settings.Save(BuildSettingsSnapshot());
                _settings.SaveStreamKeys(Profiles
                    .Where(p => p.Id != "none" && !p.KeyRetrievableViaApi && !string.IsNullOrEmpty(p.StreamKey))
                    .ToDictionary(p => p.Id, p => p.StreamKey));
            }));
        }, TaskScheduler.Default);
    }

    private CancellationTokenSource? _errorDismissCts;

    /// <summary>Clears the error banner a few seconds after it's shown -- a fresh error restarts
    /// the timer rather than stacking. Not used for the persistent "live stream just broke"
    /// case (see the ErrorEvent handler above), which is left for the user to notice.</summary>
    private void ScheduleErrorDismiss()
    {
        _errorDismissCts?.Cancel();
        var cts = new CancellationTokenSource();
        _errorDismissCts = cts;

        _ = Task.Delay(TimeSpan.FromSeconds(6), cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                if (ErrorMessage is not null) ErrorMessage = null;
            }));
        }, TaskScheduler.Default);
    }

    [RelayCommand]
    private Task PingCoreAsync() => _core.SendCommandAsync(new PingCommand());

    [RelayCommand]
    private Task RefreshSourcesAsync() => _core.SendCommandAsync(new GetSourcesCommand());

    /// <summary>Tracks the rolling 1-second rate of incoming preview frames — called from
    /// GoLiveView's own FrameReceived handler (already on the UI thread by the time it gets
    /// here) rather than this ViewModel keeping a second, independent CoreBridgeService
    /// subscription that would otherwise dispatch to the UI thread separately for every single
    /// frame. Folding it into the View's existing dispatch/drop-gate does mean this now reflects
    /// frames actually rendered rather than every frame the core sent — arguably more honest
    /// anyway, since it's meant to show what the user is actually seeing.</summary>
    public void RecordPreviewFrameReceived()
    {
        var now = DateTime.UtcNow;
        _recentFrameTimes.Enqueue(now);
        while (_recentFrameTimes.Count > 0 && (now - _recentFrameTimes.Peek()).TotalSeconds > 1)
            _recentFrameTimes.Dequeue();
        PreviewFps = _recentFrameTimes.Count;
    }

    private void OnCoreStateChanged(object? sender, EventArgs e) =>
        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(UpdateCoreStateText);

    private void OnCoreLogLineReceived(object? sender, string line) =>
        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            CoreLogLines.Add(line);
            while (CoreLogLines.Count > MaxCoreLogLines)
                CoreLogLines.RemoveAt(0);
        }));

    private void OnCoreEventReceived(object? sender, CoreEvent evt) =>
        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            switch (evt)
            {
                case PongEvent:
                    LastPongText = $"{DateTime.Now:HH:mm:ss}";
                    break;
                case SourcesEvent sources:
                    SceneEditor.AvailableSources.Clear();
                    foreach (var s in sources.Items.Where(s => s.Kind is "monitor" or "window" or "webcam"))
                    {
                        var humanizedName = HumanizeDeviceName(s.Name);
                        SceneEditor.AvailableSources.Add(s with { Name = humanizedName });
                    }

                    // Slots restored from settings had their SourceId set before the source
                    // list existed, so their display name and aspect ratio couldn't be resolved
                    // yet — fill them in now that we know what's actually available. Every
                    // scene's slots need this, not just the active one, since a scene that isn't
                    // active yet never goes through UpdateSlotCaptureAsync's normal resolution.
                    foreach (var slot in SceneEditor.Scenes.SelectMany(sc => sc.Slots).Where(s => !string.IsNullOrEmpty(s.SourceId) && !s.IsOverlay))
                    {
                        var match = SceneEditor.AvailableSources.FirstOrDefault(s => s.Id == slot.SourceId);
                        if (match is not null) slot.DisplayName = match.Name;
                        SceneEditor.TryApplyAspectRatioFromSource(slot);
                    }

                    // If the primary's actual resolution changed since we last saw it (a
                    // different monitor, or the same one at a different mode), every static
                    // overlay's cached source buffer was sized against the old resolution and
                    // may now be a poor match for its current on-screen box — re-render and
                    // re-register them all so they stay correctly capped (see
                    // GetImageOverlayCapSize). Skipped on the very first resolution discovery
                    // (there's nothing stale to refresh yet).
                    var currentPrimaryRes = SceneEditor.GetPrimaryResolution();
                    if (_lastKnownPrimaryResolution is not null && currentPrimaryRes != _lastKnownPrimaryResolution)
                        _ = SceneEditor.RefreshStaticOverlaySizesAsync();
                    _lastKnownPrimaryResolution = currentPrimaryRes;
                    break;
                case AudioDevicesEvent audioDevices:
                    AvailableAudioDevices.Clear();
                    foreach (var d in audioDevices.Items)
                        AvailableAudioDevices.Add(d);
                    RebuildAudioSources();
                    IsLoadingAudioDevices = false;
                    break;
                case CaptureStartedEvent { Width: uint w, Height: uint h } started:
                    // Only video overlays report resolution here — monitor/window/webcam
                    // sources get theirs earlier via SourcesEvent instead.
                    foreach (var slot in SceneEditor.Slots.Where(s => s.SourceId == started.SourceId))
                        SceneEditor.ApplyAspectRatio(slot, w, h);
                    break;
                case AudioDeviceLevelEvent levelEvt:
                    var matchingSource = AudioSources.FirstOrDefault(a => a.Device.Id == levelEvt.DeviceId);
                    if (matchingSource is not null) matchingSource.PeakDb = levelEvt.PeakDb;
                    break;
                case AudioDeviceVolumeEvent volEvt:
                    var matchingVolSource = AudioSources.FirstOrDefault(a => a.Device.Id == volEvt.DeviceId);
                    if (matchingVolSource is not null)
                    {
                        matchingVolSource.IsApplyingRemoteDeviceVolume = true;
                        matchingVolSource.DeviceVolumePercent = volEvt.Volume * 100.0;
                        matchingVolSource.IsDeviceMuted = volEvt.Muted;
                        matchingVolSource.IsApplyingRemoteDeviceVolume = false;
                    }
                    break;
                case StreamStartedEvent:
                    IsStreaming = true;
                    // IsTestStreaming was already set by StartStreamCoreAsync before this event
                    // ever arrives (it drives which RTMP URL variant got sent in the first
                    // place), so it's safe to just read it here for the label.
                    StatusLabel = IsRecordOnlyMode ? "Recording" : (IsTestStreaming ? "Testing" : "Live");
                    _errorDismissCts?.Cancel();
                    ErrorMessage = null;
                    _eventBus.Publish(new GoLiveStartedEvent());
                    break;
                case StreamStoppedEvent:
                    IsStreaming = false;
                    IsTestStreaming = false;
                    StatusLabel = "Idle";
                    LiveFps = 0;
                    LiveBitrateKbps = 0;
                    DroppedFrames = 0;
                    EndActiveYouTubeTestBroadcastIfAny();
                    _eventBus.Publish(new GoLiveStoppedEvent());
                    break;
                case StreamStatusEvent status:
                    LiveFps = status.Fps;
                    LiveBitrateKbps = status.BitrateKbps;
                    DroppedFrames = status.Dropped;
                    break;
                case ErrorEvent error:
                    StatusLabel = "Error";
                    ErrorMessage = error.Message;
                    // Every Event::Error the core sends is non-fatal by its own wire-protocol
                    // convention (a truly fatal issue exits the process instead) -- so the banner
                    // auto-dismisses shortly after. The one exception: an encoder error while a
                    // stream is actually live means the broadcast itself just broke, which is
                    // worth leaving on-screen until the user notices and acts.
                    if (!(IsStreaming && error.Code == "encoder_error"))
                        ScheduleErrorDismiss();
                    break;
            }

            RefreshStartStreamAvailability();
            StopStreamCommand.NotifyCanExecuteChanged();
        }));

    private static string HumanizeDeviceName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        if (name.StartsWith(@"\\.\DISPLAY", StringComparison.OrdinalIgnoreCase))
        {
            var numPart = name.Substring(11);
            if (int.TryParse(numPart, out var num))
            {
                return $"Display {num}";
            }
        }
        
        if (name.StartsWith(@"\\.\"))
        {
            return name.Substring(4);
        }
        if (name.StartsWith(@"\\?\"))
        {
            return name.Substring(4);
        }

        return name;
    }

    private void UpdateCoreStateText()
    {
        CoreStateText = _core.State switch
        {
            CoreState.NotStarted => "Not started",
            CoreState.Running => "Running",
            CoreState.Exited => "Stopped",
            CoreState.BinaryMissing => "Core binary not found",
            _ => "Unknown",
        };

        EnsureDevicesRefreshed();
    }

    private int _deviceRefreshRetriesRemaining = 2;

    /// <summary>Requests sources/audio devices whenever the core is running and we don't have
    /// them yet, retrying a couple of times a few seconds apart. This is a defensive safety net,
    /// not the primary mechanism — CoreBridgeService already queues commands sent before the
    /// core's stdin reader is confirmed up and flushes them once Ready arrives, and the Rust
    /// side now emits Ready immediately before its stdin loop starts rather than earlier — but
    /// retrying here means a launch is never left with an empty device list if something
    /// unforeseen still drops the very first request.</summary>
    private void EnsureDevicesRefreshed()
    {
        if (_core.State != CoreState.Running) return;

        var needsSources = SceneEditor.AvailableSources.Count == 0;
        var needsAudio = AvailableAudioDevices.Count == 0;
        if (!needsSources && !needsAudio) return;

        if (needsSources) _ = RefreshSourcesAsync();
        if (needsAudio) _ = RefreshAudioDevicesAsync();

        if (_deviceRefreshRetriesRemaining <= 0) return;
        _deviceRefreshRetriesRemaining--;

        _ = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(
            _ => System.Windows.Application.Current?.Dispatcher?.BeginInvoke(EnsureDevicesRefreshed),
            TaskScheduler.Default);
    }
}
