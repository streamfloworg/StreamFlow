using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Data;

namespace StreamFlow.App.Converter;

public class DoubleToPercentageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (int)Math.Round((double)value * 100, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            var dbleValue = System.Convert.ToInt32(value, CultureInfo.InvariantCulture) / 100d;
            return dbleValue;
        }
        catch { }
        return 0d;
    }
}
