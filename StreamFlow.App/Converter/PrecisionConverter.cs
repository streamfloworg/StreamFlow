using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace StreamFlow.App.Converter;

public class PrecisionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var dblParam = double.Parse((string)parameter, CultureInfo.InvariantCulture);
        var dblValue = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (dblParam > 0 && dblValue > 0)
        {
            if ((dblValue % dblParam) == 0)
            {
                return dblValue;
            }
            return ((dblValue / dblParam) * dblParam);
        }
        return 0d;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }
}
