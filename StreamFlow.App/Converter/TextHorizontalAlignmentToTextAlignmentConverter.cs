using System.Globalization;
using System.Windows;
using System.Windows.Data;

using StreamFlow.App.ViewModels.Pages;

namespace StreamFlow.App.Converter;

/// <summary>Maps TextStyle.Alignment (our own enum, shared by Text/Chat/Timer) to WPF's own
/// TextAlignment for the placement canvas's local WYSIWYG TextBlock preview — a plain assignment
/// wouldn't cross the two distinct enum types automatically.</summary>
public sealed class TextHorizontalAlignmentToTextAlignmentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        TextHorizontalAlignment.Center => TextAlignment.Center,
        TextHorizontalAlignment.Right => TextAlignment.Right,
        _ => TextAlignment.Left,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        TextAlignment.Center => TextHorizontalAlignment.Center,
        TextAlignment.Right => TextHorizontalAlignment.Right,
        _ => TextHorizontalAlignment.Left,
    };
}
