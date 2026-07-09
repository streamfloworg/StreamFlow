using System.Globalization;
using System.Windows.Data;

using StreamFlow.Core.Data;

namespace StreamFlow.App.Converter;

public class AudioViewStyleSelector : IValueConverter
{
    public Style? ListViewStyle = App.Current.FindResource("AudioListView") as Style;
    public Style? GridViewStyle = App.Current.FindResource("AudioGridView") as Style;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (ListViewStyle == null || GridViewStyle == null)
        {
            return null!;
        }
        if (value is AudioViewType audioViewType)
        {
            if (audioViewType is AudioViewType.ListView)
            {
                return ListViewStyle;
            }
        }
        return GridViewStyle;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
