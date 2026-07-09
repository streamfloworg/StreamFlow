using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Data;

using Humanizer;

namespace StreamFlow.App.Converter;

public class LookupTableHelper : List<IValueConverter>, IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter is Dictionary<Func<bool>, object> list)
        {
            return list.FirstOrDefault(x => x.Key()).Value;
        }
        else
            throw new ArgumentNullException(nameof(parameter), $"{nameof(parameter)} cannot be null. Please provide a valid lookup parameter. Valid parameter structure is Dictionary<Func<Boolean> condition, object result>");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value;
    }
}
