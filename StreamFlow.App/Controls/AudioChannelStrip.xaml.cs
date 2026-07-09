using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

using StreamFlow.App.ViewModels.Pages;

using UserControl = System.Windows.Controls.UserControl;

namespace StreamFlow.App.Controls;

/// <summary>Combined level-meter + volume-fader + solo/mute control for one selected audio
/// device, shown in the row of channel strips under the Go Live preview. DataContext is the
/// owning StreamFlow.App.ViewModels.Pages.AudioSourceItem.</summary>
public partial class AudioChannelStrip : UserControl
{
    public AudioChannelStrip()
    {
        InitializeComponent();
        // Drives meter ballistics (see AudioSourceItem.TickMeterBallistics) once per rendered
        // frame — the underlying peak data only arrives ~33 times/sec, but easing toward it on
        // every frame is what makes the meter read as continuously live instead of stepping.
        // Only subscribed while this strip is actually on screen.
        Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (DataContext is AudioSourceItem item) item.TickMeterBallistics();
    }
}

/// <summary>Converts a 0.0-1.0 fraction to pixels given a live "total" binding (the meter host's
/// own ActualHeight) — same shape as PercentToDynamicPixelConverter in GoLiveView.xaml.cs, just
/// 0-1 instead of 0-100 since MeterFillFraction is already a fraction.</summary>
public sealed class FractionToPixelConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var fraction = values.Length > 0 && values[0] is double f ? f : 0;
        var total = values.Length > 1 && values[1] is double t ? t : 0;
        return fraction * total;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>(1 - fraction) × total — sizes the meter's top "cover" rectangle, which hides
/// everything above the current level. The LED bar itself is a full-height fixed gradient
/// (green with an amber top zone) so each color band stays anchored to its position on the
/// scale; revealing it from the bottom by shrinking a cover is what makes the amber zone only
/// light up when the level actually reaches it, instead of the gradient stretching with the
/// fill the way a bottom-anchored fill rectangle would.</summary>
public sealed class InverseFractionToPixelConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var fraction = values.Length > 0 && values[0] is double f ? f : 0;
        var total = values.Length > 1 && values[1] is double t ? t : 0;
        return Math.Max(0, (1 - Math.Clamp(fraction, 0, 1)) * total);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Builds a Rect from a live width/height binding pair for a RectangleGeometry clip —
/// used to clip the meter's flat-edged LED fill/cover to the track's rounded shape. Border does
/// NOT auto-clip its child to CornerRadius in WPF (unlike WinUI/UWP's Border, which does); an
/// explicit RectangleGeometry clip sized to match is the standard WPF workaround. RadiusX/RadiusY
/// on the geometry stay in absolute pixels regardless of the Rect's size, so — unlike a
/// Stretch-scaled VisualBrush mask — the corner curve never distorts as height changes.</summary>
public sealed class SizeToRectConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var width = values.Length > 0 && values[0] is double w ? Math.Max(0, w) : 0;
        var height = values.Length > 1 && values[1] is double h ? Math.Max(0, h) : 0;
        return new Rect(0, 0, width, height);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
