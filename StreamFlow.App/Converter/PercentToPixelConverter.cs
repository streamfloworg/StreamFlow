using System.Globalization;
using System.Windows.Data;

namespace StreamFlow.App.Converter;

/// <summary>Converts a 0-100 percent value to pixels given a total-pixels ConverterParameter.
/// Only usable where the total is genuinely fixed (e.g. the canvas's reference width, which
/// never changes) — ConverterParameter can't be a live binding, so anywhere the total itself
/// varies (the canvas's height, which tracks the primary's real aspect ratio) needs
/// <see cref="PercentToDynamicPixelConverter"/> instead.</summary>
public sealed class PercentToPixelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = value is double d ? d : 0;
        var total = parameter is string s ? double.Parse(s, culture) : 0;
        return percent / 100.0 * total;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
