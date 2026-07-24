using StreamFlow.App.Services.Core;

namespace StreamFlow.App.ViewModels.Pages;

/// <summary>Checkbox-selectable wrapper around a NativeAudioDevice for the Audio Sources list —
/// NativeAudioDevice itself is an immutable record with no place to hang selection/mix state.
/// Also backs the channel-strip control shown under the preview for each selected device.</summary>
public partial class AudioSourceItem : ObservableObject
{
    public NativeAudioDevice Device { get; }

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Editable label shown in the channel strip — defaults to the device's own OS
    /// name, but can be renamed and is persisted (by device id) via GoLiveSettings.
    /// AudioDeviceDisplayNames. See ResetDisplayName for reverting back to the default.</summary>
    [ObservableProperty]
    private string _displayName;

    /// <summary>Whether DisplayName currently differs from the device's own name — drives
    /// whether the channel strip shows a "reset to default" affordance at all.</summary>
    public bool IsRenamed => !string.Equals(DisplayName, Device.Name, StringComparison.Ordinal);

    partial void OnDisplayNameChanged(string value) => OnPropertyChanged(nameof(IsRenamed));

    [RelayCommand]
    private void ResetDisplayName() => DisplayName = Device.Name;

    [RelayCommand]
    private void Remove() => IsSelected = false;

    /// <summary>0-100, linear amplitude (not a dB-scaled fader position) — Gain below derives
    /// straight from this as a fraction, and DisplayDb derives the dB text from that same gain.
    /// Kept simple deliberately: a proper dB-scaled fader (e.g. -60..+12 with 0dB near the top)
    /// is a reasonable follow-up if the linear feel doesn't match expectations in practice.</summary>
    [ObservableProperty]
    private double _volumePercent = 100;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private bool _isSolo;

    /// <summary>Live peak level in dBFS from the most recent AudioDeviceLevelEvent for this
    /// device — float.NegativeInfinity (silence) until the first one arrives.</summary>
    [ObservableProperty]
    private float _peakDb = float.NegativeInfinity;

    /// <summary>0-100 — the device's actual OS-level (Volume Mixer) master volume, distinct
    /// from VolumePercent above (which only scales this device's contribution to this app's
    /// own stream mix). Reflects AudioDeviceVolumeEvent when it changes externally (another
    /// app, physical volume keys, Windows Settings), and round-trips back out as
    /// SetDeviceVolumeCommand when the user drags it — see IsApplyingRemoteDeviceVolume.</summary>
    [ObservableProperty]
    private double _deviceVolumePercent = 100;

    /// <summary>The device's actual OS-level mute state (distinct from IsMuted above, which is
    /// this app's own stream-mix mute).</summary>
    [ObservableProperty]
    private bool _isDeviceMuted;

    [ObservableProperty]
    private bool _isDuckingTrigger;

    [ObservableProperty]
    private bool _isDuckingTarget;

    partial void OnIsDuckingTriggerChanged(bool value)
    {
        if (value && IsDuckingTarget)
        {
            IsDuckingTarget = false;
        }
    }

    partial void OnIsDuckingTargetChanged(bool value)
    {
        if (value && IsDuckingTrigger)
        {
            IsDuckingTrigger = false;
        }
    }

    /// <summary>Set by GoLiveViewModel while applying an incoming AudioDeviceVolumeEvent to
    /// DeviceVolumePercent/IsDeviceMuted, so its PropertyChanged subscriber can tell "core just
    /// told us this" apart from "the user just dragged this" — without it, applying the event
    /// would immediately round-trip a SetDeviceVolume/SetDeviceMute command right back at core
    /// for a change core itself just reported.</summary>
    public bool IsApplyingRemoteDeviceVolume { get; set; }

    public AudioSourceItem(NativeAudioDevice device, bool isSelected, string? displayName = null)
    {
        Device = device;
        _isSelected = isSelected;
        _displayName = displayName ?? device.Name;
    }

    /// <summary>Grouping key for the Audio Devices panel — selected devices move into their own
    /// "Active" group above the Kind-based groups, so a checked device stays visible/findable
    /// without hunting through output/microphone/capture. See AudioSourcesView's live-grouping
    /// setup in GoLiveViewModel.cs, which re-evaluates this whenever IsSelected changes.</summary>
    public string GroupKey => IsSelected ? "Active" : Device.Kind;

    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(GroupKey));

    /// <summary>Linear gain multiplier sent to the core's mixer — 1.0 = unity at 100%.</summary>
    public float Gain => (float)(VolumePercent / 100.0);

    /// <summary>Volume expressed in dB for the readout text, derived from the same linear gain
    /// the mixer actually uses (so the two displayed numbers can never disagree).</summary>
    public double DisplayDb => VolumePercent > 0 ? 20.0 * Math.Log10(VolumePercent / 100.0) : double.NegativeInfinity;

    /// <summary>0.0-1.0 fill fraction for the level meter, mapping PeakDb over a -60..0 dBFS
    /// range (below -60 reads as silence/empty, matching typical consumer meter conventions).
    /// This is the raw target value — the UI binds SmoothedMeterFillFraction below instead, so
    /// the meter doesn't visibly step at the ~33Hz rate new peak data actually arrives at.</summary>
    public double MeterFillFraction => double.IsNegativeInfinity(PeakDb) ? 0.0 : Math.Clamp((PeakDb + 60.0) / 60.0, 0.0, 1.0);

    /// <summary>What the meter actually renders — eased toward MeterFillFraction once per
    /// rendered frame by AudioChannelStrip (see TickMeterBallistics), instead of jumping
    /// straight to each new value. Fast attack / slower decay, like a real VU meter.</summary>
    [ObservableProperty]
    private double _smoothedMeterFillFraction;

    partial void OnVolumePercentChanged(double value)
    {
        OnPropertyChanged(nameof(Gain));
        OnPropertyChanged(nameof(DisplayDb));
    }

    partial void OnPeakDbChanged(float value) => OnPropertyChanged(nameof(MeterFillFraction));

    /// <summary>Advances SmoothedMeterFillFraction one step toward the current MeterFillFraction
    /// target. Called once per rendered frame (see AudioChannelStrip's CompositionTarget.Rendering
    /// hook) — asymmetric rates so a rising level catches up almost immediately (feels responsive)
    /// while a falling level eases down gradually (feels like a real meter, not a jittery snap).</summary>
    public void TickMeterBallistics()
    {
        var target = MeterFillFraction;
        var rate = target > SmoothedMeterFillFraction ? 0.6 : 0.15;
        SmoothedMeterFillFraction += (target - SmoothedMeterFillFraction) * rate;
    }
}
