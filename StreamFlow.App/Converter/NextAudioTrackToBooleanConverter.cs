using System.Globalization;
using System.Windows.Data;
using StreamFlow.Core.AudioHandling;

namespace StreamFlow.App.Converter;

public class NextAudioTrackToBooleanConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not AudioTrack track)
        {
            return false;
        }

        if (track.NextAudioTrack == null || !track.NextAudioTrack.ValidPath)
        {
            return false;
        }

        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
