using System.Globalization;
using System.Windows.Data;

namespace StreamFlow.App.Converter;
public class AddValueConverter : IValueConverter
{
     
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double returnedValue = 0;
        if (value is double intValue)
        {
            double.TryParse(parameter as string, out var paramValue);
            returnedValue = intValue + paramValue;
        }

        return returnedValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
