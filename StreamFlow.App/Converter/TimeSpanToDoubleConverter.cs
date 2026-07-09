using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Data;

namespace StreamFlow.App.Converter;

public class TimeSpanToDoubleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TimeSpan ts)
        {
            return ts.TotalSeconds;
        }
        return 0.0;
    }

    private static bool CanConvert(object value)
    {
        return value is double;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (CanConvert(value))
        {
            return TimeSpan.FromSeconds((double)value);
        }
        return TimeSpan.Zero;
    }
}
