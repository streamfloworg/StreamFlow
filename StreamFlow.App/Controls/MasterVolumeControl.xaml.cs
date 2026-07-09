using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

using StreamFlow.App.ViewModels.Pages;

using UserControl = System.Windows.Controls.UserControl;

namespace StreamFlow.App.Controls;

/// <summary>Always-present master stream-mix gain control shown alongside the per-device
/// channel strips on the Go Live page. Unlike AudioChannelStrip, this isn't bound to any single
/// AudioSourceItem — Value is a plain two-way DependencyProperty the caller binds directly to
/// GoLiveViewModel.MasterVolumePercent, and the meter reflects the loudest currently-selected,
/// unmuted channel's own (already ballistics-smoothed) level rather than a true post-mix peak —
/// the core doesn't currently report a combined-mix level, and approximating one client-side
/// from each channel's own smoothed value is a reasonable stand-in without needing a new IPC
/// event.</summary>
public partial class MasterVolumeControl : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(MasterVolumeControl),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>The channel strips whose levels this control's meter aggregates — bound by the
    /// caller to GoLiveViewModel.SelectedAudioChannels.</summary>
    public static readonly DependencyProperty SourceChannelsProperty = DependencyProperty.Register(
        nameof(SourceChannels), typeof(ObservableCollection<AudioSourceItem>), typeof(MasterVolumeControl));

    public ObservableCollection<AudioSourceItem>? SourceChannels
    {
        get => (ObservableCollection<AudioSourceItem>?)GetValue(SourceChannelsProperty);
        set => SetValue(SourceChannelsProperty, value);
    }

    private static readonly DependencyPropertyKey MeterFillFractionPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(MeterFillFraction), typeof(double), typeof(MasterVolumeControl), new PropertyMetadata(0.0));

    public static readonly DependencyProperty MeterFillFractionProperty = MeterFillFractionPropertyKey.DependencyProperty;

    public double MeterFillFraction => (double)GetValue(MeterFillFractionProperty);

    public MasterVolumeControl()
    {
        InitializeComponent();
        // Same per-rendered-frame tick pattern as AudioChannelStrip's own meter — each channel's
        // SmoothedMeterFillFraction is already eased by that channel's own AudioChannelStrip
        // instance; this just re-samples the loudest of them every frame rather than adding a
        // second, redundant smoothing stage on top.
        Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var fraction = 0.0;
        if (SourceChannels is not null)
        {
            foreach (var channel in SourceChannels)
            {
                if (channel.IsMuted) continue;
                if (channel.SmoothedMeterFillFraction > fraction) fraction = channel.SmoothedMeterFillFraction;
            }
        }
        SetValue(MeterFillFractionPropertyKey, fraction);
    }
}
