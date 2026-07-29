using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StreamFlow.App.Converter;

/// <summary>Hides an advanced overlay's (Alert) rendered content while streaming/recording,
/// regardless of the live-edit lock — unlike the outline/handles/header (see GoLiveView.xaml's
/// MoveThumb triggers), the actual content stays hidden even with editing unlocked; only its
/// position should be visible for repositioning, not a static preview of what only actually
/// appears during a real triggered animation.
/// Bindings: [0] SourceSlot.IsPartOfAdvancedOverlay, [1] GoLiveViewModel.IsStreaming.</summary>
public sealed class AdvancedOverlayContentVisibilityConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is [bool isPartOfAdvancedOverlay, bool isStreaming] && isPartOfAdvancedOverlay && isStreaming)
            return Visibility.Collapsed;

        return Visibility.Visible;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
