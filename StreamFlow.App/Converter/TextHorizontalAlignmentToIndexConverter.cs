using System.Globalization;
using System.Windows.Data;

using StreamFlow.App.ViewModels.Pages;

namespace StreamFlow.App.Converter;

/// <summary>Maps TextStyle.Alignment to/from a ComboBox's SelectedIndex (Left=0, Center=1,
/// Right=2) — mirrors TimerModeToIndexConverter's approach for the same "enum via ComboBox
/// index" binding shape.</summary>
public sealed class TextHorizontalAlignmentToIndexConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TextHorizontalAlignment alignment ? (int)alignment : 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int index ? (TextHorizontalAlignment)index : TextHorizontalAlignment.Left;
}
