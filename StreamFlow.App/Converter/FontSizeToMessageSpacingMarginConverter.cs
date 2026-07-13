using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StreamFlow.App.Converter;

/// <summary>Maps TextStyle.FontSize to a top-only Thickness for the chat overlay's local preview
/// — proportional (FontSize/6), matching OverlayContentRenderer.RenderChatToBgra's own inter-
/// message spacing formula exactly, rather than the flat 8-unit constant both used before (which
/// didn't shrink/grow as the user adjusted font size).</summary>
public sealed class FontSizeToMessageSpacingMarginConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double fontSize ? new Thickness(0, fontSize / 6, 0, 0) : new Thickness(0, 8, 0, 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
