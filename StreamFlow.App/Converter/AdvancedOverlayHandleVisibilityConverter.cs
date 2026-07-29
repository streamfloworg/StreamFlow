using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StreamFlow.App.Converter;

/// <summary>Resize-handle counterpart of AdvancedOverlayContentVisibilityConverter: a handle
/// still needs the slot selected to show at all, but for an advanced overlay (Alert) it's also
/// suppressed while streaming/recording unless editing has been explicitly unlocked — matching
/// MoveThumb's own outline/header suppression in GoLiveView.xaml (see its ControlTemplate.Triggers)
/// so all of a locked, live advanced overlay's editing chrome disappears together, not just the
/// outline while these corner/edge squares keep poking out.
/// Bindings: [0] SourceSlot.IsSelected, [1] SourceSlot.IsPartOfAdvancedOverlay,
/// [2] GoLiveViewModel.IsStreaming, [3] GoLiveViewModel.IsLiveEditUnlocked.</summary>
public sealed class AdvancedOverlayHandleVisibilityConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [bool isSelected, bool isPartOfAdvancedOverlay, bool isStreaming, bool isLiveEditUnlocked])
            return Visibility.Collapsed;

        if (!isSelected) return Visibility.Collapsed;

        var suppressed = isPartOfAdvancedOverlay && isStreaming && !isLiveEditUnlocked;
        return suppressed ? Visibility.Collapsed : Visibility.Visible;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
