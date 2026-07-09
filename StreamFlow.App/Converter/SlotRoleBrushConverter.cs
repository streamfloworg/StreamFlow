using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace StreamFlow.App.Converter;

public sealed class SlotRoleBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush PrimaryBrush = new(System.Windows.Media.Color.FromRgb(0x2E, 0x6F, 0xB8));
    private static readonly SolidColorBrush PipBrush = new(System.Windows.Media.Color.FromRgb(0xB8, 0x6F, 0x2E));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? PrimaryBrush : PipBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
