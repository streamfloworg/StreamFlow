using System.Globalization;
using System.Windows.Data;

namespace StreamFlow.App.Converter;

/// <summary>Maps SourceSlot.RotationDegrees (0/90/180/270) to/from a ComboBox's SelectedIndex
/// (0/1/2/3) — the two happen to be a simple *90 relationship since rotation is restricted to
/// exact 90-degree steps (see RotationDegrees' own doc comment for why).</summary>
public sealed class RotationDegreesToIndexConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int degrees ? degrees / 90 : 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int index ? index * 90 : 0;
}
