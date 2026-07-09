using System.Globalization;
using System.Windows.Data;

using StreamFlow.Core.AudioHandling;

namespace StreamFlow.App.Converter;
public class AudioIsPlayingConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is Audio audio && values[1] is Audio currentPlaying)
        {
            if (audio is AudioTrack track && currentPlaying != null && currentPlaying.Name == track.Name)
            {
                return true;
            }
        }
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
