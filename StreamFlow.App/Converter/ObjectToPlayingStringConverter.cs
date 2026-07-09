using System.Globalization;
using System.Windows.Data;

namespace StreamFlow.App.Converter;

public class ObjectToPlayingStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value.ToString() == "")
        {
            return "No audio playing";
        }

        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
