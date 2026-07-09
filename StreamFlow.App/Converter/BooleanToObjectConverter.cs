using System.Globalization;
using System.Windows.Data;

namespace StreamFlow.App.Converter;
public class BooleanToObjectConverter : IValueConverter
{
    public object? TrueValue { get; set; }
    public object? FalseValue { get; set; }

    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is not null ? System.Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? TrueValue : FalseValue : null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
