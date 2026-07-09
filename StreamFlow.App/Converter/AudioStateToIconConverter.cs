using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

using StreamFlow.Core.AudioHandling;

namespace StreamFlow.App.Converter;
public class AudioStateToIconConverter : IMultiValueConverter
{
     
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var returnedIconData = App.Current.FindResource("PlayIconData") as Geometry;
        if (values[0] is Audio audio && values[1] is Audio currentPlaying)
        {
            if (audio is AudioTrack track && currentPlaying?.Name == track.Name)
            {
                returnedIconData = App.Current.FindResource("PauseIconData") as Geometry;
            }
        }
        return returnedIconData;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
