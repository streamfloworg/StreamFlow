using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Data;

using Humanizer;

namespace StreamFlow.App.Converter;

public class StringHelper : List<IValueConverter>, IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string stringValue)
        {
            if ((string)parameter == "Case")
            {
                var str = stringValue.Trim().Transform(To.TitleCase);
                return str;
            }
            return stringValue.Trim();
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value;
    }
}
