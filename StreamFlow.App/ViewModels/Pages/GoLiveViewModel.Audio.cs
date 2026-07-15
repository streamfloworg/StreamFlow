using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

using StreamFlow.App.Services;
using StreamFlow.App.Services.Core;

namespace StreamFlow.App.ViewModels.Pages;

/// <summary>Audio device enumeration/selection for the Audio Sources panel and channel-strip
/// row — device volume/mute/level event handling, and page-active tracking that gates whether
/// audio monitor sessions (live level meters) actually run.</summary>
public partial class GoLiveViewModel
{
    /// <summary>Refreshed alongside AvailableSources so it's populated well before "Go Live"
    /// is ever clicked — StartStreamAsync reads straight from this rather than awaiting a
    /// fresh round trip, matching how AvailableSources is already used elsewhere.</summary>
    public ObservableCollection<NativeAudioDevice> AvailableAudioDevices { get; } = [];

    /// <summary>Drives the loading spinner in the Audio Devices panel, which — unlike most
    /// device-dependent UI — is always visible rather than hidden while empty, so an empty list
    /// needs a visible "still loading" cue rather than reading as "no devices exist." Set true
    /// whenever a refresh is requested (RefreshAudioDevicesAsync/EnsureDevicesRefreshed) and
    /// cleared once an AudioDevicesEvent actually arrives.</summary>
    [ObservableProperty]
    private bool _isLoadingAudioDevices;

    /// <summary>Checkbox-selectable view over AvailableAudioDevices for the Audio Sources
    /// panel — rebuilt (preserving existing selections by device id) whenever a fresh
    /// AudioDevicesEvent arrives. StartStreamAsync reads the selected ids straight from here.</summary>
    public ObservableCollection<AudioSourceItem> AudioSources { get; } = [];

    public ObservableCollection<AudioSourceItem> DesignTimeAudioSources { get; } = [new AudioSourceItem(new NativeAudioDevice("0", "Test Audio Device", "Output", true), true)];

    /// <summary>AudioSources grouped by device kind (output/microphone/capture) for the Audio
    /// Sources panel — a live view, so it stays in sync automatically as AudioSources is
    /// rebuilt (same GetDefaultView pattern AudioViewModel.AudioListCollectionView uses).</summary>
    public ICollectionView AudioSourcesView { get; }

    public ICollectionView DesignTimeAudioSourcesView { get; }

    /// <summary>Persisted selection loaded at startup, applied the first time AudioSources is
    /// populated this session (see RebuildAudioSources) — null means "never touched the
    /// picker," which falls back to whatever's currently marked default.</summary>
    private List<string>? _persistedSelectedAudioDeviceIds;

    /// <summary>Persisted device-id → custom display name map, applied the first time
    /// AudioSources is populated this session — see RebuildAudioSources and
    /// AudioSourceItem.DisplayName.</summary>
    private Dictionary<string, string>? _persistedAudioDeviceDisplayNames;

    private HashSet<string> _persistedDuckingTargetDeviceIds = [];
    private bool _isRebuildingAudioSources;

    /// <summary>Selected devices in the order they were checked — feeds the row of channel-strip
    /// controls under the preview, each new one appended to the right of existing ones. Distinct
    /// from AudioSourcesView (which is grouped by kind for the picker, not check order). A
    /// device-list refresh replaces the AudioSourceItem instances and can't recover true
    /// historical check order, so it's resynced to current selection order at that point —
    /// incremental checking/unchecking during normal use preserves order exactly.</summary>
    public ObservableCollection<AudioSourceItem> SelectedAudioChannels { get; } = [];

    /// <summary>Overall stream-mix gain (0-100) — a hardware-mixer-style master fader that's
    /// always present regardless of which/how many channel strips are selected, distinct from
    /// each AudioSourceItem's own VolumePercent (that device's individual contribution). See
    /// EffectiveGain, which is what actually gets sent to the core.</summary>
    [ObservableProperty]
    private double _masterVolumePercent = 100;

    /// <summary>A channel's own gain scaled by the master fader — this, not AudioSourceItem.Gain
    /// directly, is what every SetAudioMixCommand/AudioSourceConfig actually sends to the core.</summary>
    private float EffectiveGain(AudioSourceItem item) => item.Gain * (float)(MasterVolumePercent / 100.0);

    partial void OnMasterVolumePercentChanged(double value)
    {
        if (_isInitializing) return;
        // Re-push every currently-active channel's effective gain — the master fader affects
        // all of them at once, not just whichever one the user happens to be dragging.
        foreach (var item in SelectedAudioChannels)
            _ = _core.SendCommandAsync(new SetAudioMixCommand(item.Device.Id, EffectiveGain(item), item.IsMuted, item.IsSolo));
        ScheduleSaveSettings();
    }

    [RelayCommand]
    private Task RefreshAudioDevicesAsync()
    {
        IsLoadingAudioDevices = true;
        return _core.SendCommandAsync(new GetAudioDevicesCommand());
    }

    /// <summary>Hard cap on simultaneously-selected audio devices — each one is its own live
    /// WASAPI capture/mix source, so an unbounded selection scales directly into Core CPU cost.
    /// Enforced both interactively (see the IsSelected handler below) and here for whatever was
    /// last persisted, in case a settings file predates this cap or was hand-edited.</summary>
    public const int MaxSelectedAudioDevices = 8;

    /// <summary>Rebuilds AudioSources from the current AvailableAudioDevices, preserving
    /// whatever's already selected (by device id) across refreshes. The very first time this
    /// runs each session, selection instead comes from the persisted settings — falling back to
    /// "whatever's currently marked default" if the user has never touched the picker.</summary>
    private void RebuildAudioSources()
    {
        _isRebuildingAudioSources = true;
        try
        {
            var previouslySelected = AudioSources.Where(a => a.IsSelected).Select(a => a.Device.Id).ToHashSet();
            var previousDisplayNames = AudioSources.Where(a => a.IsRenamed).ToDictionary(a => a.Device.Id, a => a.DisplayName);
            var isFirstPopulation = _persistedSelectedAudioDeviceIds is not null;
            var persistedIds = _persistedSelectedAudioDeviceIds?.Take(MaxSelectedAudioDevices).ToHashSet();

            AudioSources.Clear();
            foreach (var device in AvailableAudioDevices)
            {
                var isSelected = isFirstPopulation
                    ? persistedIds!.Contains(device.Id)
                    : previouslySelected.Count > 0
                        ? previouslySelected.Contains(device.Id)
                        : device.IsDefault && device.Kind is "output" or "microphone";

                // A refresh preserves whatever rename is already live in memory; the very first
                // population this session instead comes from persisted settings.
                var displayName = isFirstPopulation
                    ? _persistedAudioDeviceDisplayNames?.GetValueOrDefault(device.Id)
                    : previousDisplayNames.GetValueOrDefault(device.Id);

                var item = new AudioSourceItem(device, isSelected, displayName)
                {
                    IsDuckingTrigger = (device.Id == DuckingTriggerDeviceId),
                    IsDuckingTarget = _persistedDuckingTargetDeviceIds.Contains(device.Id)
                };
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(AudioSourceItem.DeviceVolumePercent) or nameof(AudioSourceItem.IsDeviceMuted))
                    {
                        // Suppressed while applying an incoming AudioDeviceVolumeEvent — otherwise
                        // reflecting a change core just reported would immediately send it right
                        // back as a command.
                        if (item.IsApplyingRemoteDeviceVolume) return;
                        _ = e.PropertyName == nameof(AudioSourceItem.DeviceVolumePercent)
                            ? _core.SendCommandAsync(new SetDeviceVolumeCommand(item.Device.Id, (float)(item.DeviceVolumePercent / 100.0)))
                            : _core.SendCommandAsync(new SetDeviceMuteCommand(item.Device.Id, item.IsDeviceMuted));
                        return;
                    }

                    if (e.PropertyName is nameof(AudioSourceItem.VolumePercent) or nameof(AudioSourceItem.IsMuted) or nameof(AudioSourceItem.IsSolo))
                    {
                        // Fire-and-forget, same as SetDeviceVolume/SetDeviceMute above — the core
                        // no-ops this if no stream is active or this device isn't part of its mix,
                        // so it's safe to always send rather than gating on IsStreaming here.
                        _ = _core.SendCommandAsync(new SetAudioMixCommand(item.Device.Id, EffectiveGain(item), item.IsMuted, item.IsSolo));
                        return;
                    }

                    if (e.PropertyName is nameof(AudioSourceItem.IsDuckingTarget) or nameof(AudioSourceItem.IsDuckingTrigger))
                    {
                        if (!_isRebuildingAudioSources)
                        {
                            if (e.PropertyName == nameof(AudioSourceItem.IsDuckingTrigger))
                            {
                                if (item.IsDuckingTrigger)
                                {
                                    DuckingTriggerDeviceId = item.Device.Id;
                                }
                                else if (DuckingTriggerDeviceId == item.Device.Id)
                                {
                                    DuckingTriggerDeviceId = null;
                                }
                            }
                            SendDuckingRules();
                            ScheduleSaveSettings();
                        }
                        return;
                    }

                    if (e.PropertyName == nameof(AudioSourceItem.DisplayName))
                    {
                        ScheduleSaveSettings();
                        return;
                    }

                    if (e.PropertyName != nameof(AudioSourceItem.IsSelected)) return;

                    if (item.IsSelected && !SelectedAudioChannels.Contains(item) && SelectedAudioChannels.Count >= MaxSelectedAudioDevices)
                    {
                        // Revert — re-enters this same handler with IsSelected now false, which
                        // falls through to the plain "remove" branch below as a harmless no-op
                        // (it was never added to SelectedAudioChannels in the first place).
                        item.IsSelected = false;
                        _ = _dialogs.WarningAsync("Audio Devices",
                            $"Only {MaxSelectedAudioDevices} audio devices can be selected at once — each one is a live capture/mix source, and more starts costing Core real CPU. Deselect one first.");
                        return;
                    }

                    ScheduleSaveSettings();
                    if (item.IsSelected)
                    {
                        if (!SelectedAudioChannels.Contains(item)) SelectedAudioChannels.Add(item);
                    }
                    else
                    {
                        SelectedAudioChannels.Remove(item);
                    }

                    if (!_isGoLivePageActive) return;
                    _ = item.IsSelected
                        ? _core.SendCommandAsync(new StartAudioMonitorCommand(item.Device.Id))
                        : _core.SendCommandAsync(new StopAudioMonitorCommand(item.Device.Id));
                };
                AudioSources.Add(item);
            }

            _persistedSelectedAudioDeviceIds = null; // only applies to the first population

            // A refresh replaces every AudioSourceItem instance, so true historical check order
            // can't survive it — resync to current selection (in AudioSources' own enumeration
            // order) rather than leaving stale/removed instances behind.
            SelectedAudioChannels.Clear();
            foreach (var selected in AudioSources.Where(a => a.IsSelected))
                SelectedAudioChannels.Add(selected);

            // The rebuilt items above are already selected at construction time (no property-changed
            // notification fires for that), so the hook that starts a monitor on selection wouldn't
            // otherwise catch devices that come back pre-selected after a refresh.
            if (_isGoLivePageActive)
            {
                foreach (var selected in AudioSources.Where(a => a.IsSelected))
                    _ = _core.SendCommandAsync(new StartAudioMonitorCommand(selected.Device.Id));
            }
        }
        finally
        {
            _isRebuildingAudioSources = false;
        }
        SendDuckingRules();
    }

    /// <summary>Tracks whether the Go Live page is the current navigation target — audio
    /// monitor sessions (live level meters) only run while it's true, so idle capture doesn't
    /// happen just because a device is checked and the user is on a different page. Set from
    /// GoLiveView's Loaded/Unloaded (see OnNavigatedToAsync/OnNavigatedFromAsync below).</summary>
    private bool _isGoLivePageActive;

    public override Task OnNavigatedToAsync()
    {
        _isGoLivePageActive = true;
        foreach (var selected in AudioSources.Where(a => a.IsSelected))
            _ = _core.SendCommandAsync(new StartAudioMonitorCommand(selected.Device.Id));
        return Task.CompletedTask;
    }

    public override Task OnNavigatedFromAsync()
    {
        _isGoLivePageActive = false;
        foreach (var selected in AudioSources.Where(a => a.IsSelected))
            _ = _core.SendCommandAsync(new StopAudioMonitorCommand(selected.Device.Id));
        return Task.CompletedTask;
    }

    [ObservableProperty] private bool _isDuckingEnabled;
    [ObservableProperty] private string? _duckingTriggerDeviceId;
    [ObservableProperty] private float _duckingThresholdDb = -30.0f;
    [ObservableProperty] private float _duckingDepth = 0.8f;
    [ObservableProperty] private float _duckingAttackMs = 10.0f;
    [ObservableProperty] private float _duckingReleaseMs = 300.0f;
    [ObservableProperty] private float _duckingHoldMs = 100.0f;

    partial void OnDuckingTriggerDeviceIdChanged(string? value)
    {
        foreach (var item in AudioSources)
        {
            item.IsDuckingTrigger = (item.Device.Id == value);
        }
        SendDuckingRules();
        ScheduleSaveSettings();
    }

    partial void OnIsDuckingEnabledChanged(bool value) { SendDuckingRules(); ScheduleSaveSettings(); }
    partial void OnDuckingThresholdDbChanged(float value) { SendDuckingRules(); ScheduleSaveSettings(); }
    partial void OnDuckingDepthChanged(float value) { SendDuckingRules(); ScheduleSaveSettings(); }
    partial void OnDuckingAttackMsChanged(float value) { SendDuckingRules(); ScheduleSaveSettings(); }
    partial void OnDuckingReleaseMsChanged(float value) { SendDuckingRules(); ScheduleSaveSettings(); }
    partial void OnDuckingHoldMsChanged(float value) { SendDuckingRules(); ScheduleSaveSettings(); }

    private void SendDuckingRules()
    {
        if (_isInitializing || _isRebuildingAudioSources) return;

        if (!IsDuckingEnabled || string.IsNullOrEmpty(DuckingTriggerDeviceId))
        {
            _ = _core.SendCommandAsync(new SetDuckingCommand([]));
            return;
        }

        // Gather target device IDs
        var targetIds = AudioSources
            .Where(a => a.IsSelected && a.IsDuckingTarget && a.Device.Id != DuckingTriggerDeviceId)
            .Select(a => a.Device.Id)
            .ToArray();

        if (targetIds.Length == 0)
        {
            _ = _core.SendCommandAsync(new SetDuckingCommand([]));
            return;
        }

        var rule = new DuckingRuleConfig(
            TriggerDeviceId: DuckingTriggerDeviceId,
            TargetDeviceIds: targetIds,
            ThresholdDb: DuckingThresholdDb,
            DuckDepth: DuckingDepth,
            AttackMs: DuckingAttackMs,
            ReleaseMs: DuckingReleaseMs,
            HoldMs: DuckingHoldMs
        );

        _ = _core.SendCommandAsync(new SetDuckingCommand([rule]));
    }
}
