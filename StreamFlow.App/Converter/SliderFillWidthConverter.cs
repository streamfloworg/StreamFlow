using System;
using System.Globalization;
using System.Windows.Data;

namespace StreamFlow.App.Converter;

public sealed class SliderFillWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length < 4)
        {
            return 0.0;
        }

        var total = ToDouble(values[0]);
        var min = ToDouble(values[1]);
        var max = ToDouble(values[2]);
        var val = ToDouble(values[3]);

        if (max <= min || total <= 0)
        {
            return 0.0;
        }

        var t = (val - min) / (max - min);
        if (double.IsNaN(t) || double.IsInfinity(t))
        {
            t = 0;
        }

        t = Math.Clamp(t, 0.0, 1.0);
        var px = total * t;
        // Ensure a minimal visible width so the bar is discoverable even at minimum
        const double minPx = 2.0;
        if (px < minPx && total >= minPx)
        {
            px = minPx;
        }

        if (px > total)
        {
            px = total;
        }

        return px;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static double ToDouble(object o)
    {
        if (o == null)
        {
            return 0.0;
        }

        try { return System.Convert.ToDouble(o, CultureInfo.InvariantCulture); } catch { return 0.0; }
    }
}

