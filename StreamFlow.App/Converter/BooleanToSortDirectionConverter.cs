using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;

namespace StreamFlow.App.Converter;

internal sealed class BooleanToSortDirectionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if ((ListSortDirection)value == ListSortDirection.Descending)
        {
            return true;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if ((bool)value == true)
        {
            return ListSortDirection.Descending;
        }
        return ListSortDirection.Ascending;
    }
}
