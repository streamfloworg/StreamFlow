using System.Globalization;
using System.Windows.Data;

namespace StreamFlow.App.Converter;

/// <summary>Converts a 0-100 percent value to a 0.0-1.0 fraction — used to feed a percent-scale
/// property (e.g. SourceSlot.OpacityPercent) straight into a WPF Opacity property, which expects
/// 0.0-1.0.</summary>
public sealed class PercentToFractionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double percent ? percent / 100.0 : 1.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
