using System.Globalization;
using System.Windows.Data;

namespace StreamFlow.App.Converter;

/// <summary>Converts a 0-100 percent value to pixels given a live "total" binding rather than a
/// fixed ConverterParameter — needed wherever the total itself varies, e.g. the canvas's height
/// (see SourceSlot.CanvasHeight). Bindings: [0] percent, [1] total.</summary>
public sealed class PercentToDynamicPixelConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = values.Length > 0 && values[0] is double p ? p : 0;
        var total = values.Length > 1 && values[1] is double t ? t : 0;
        return percent / 100.0 * total;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
