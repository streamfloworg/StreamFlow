using System.Globalization;
using System.Windows.Data;
using StreamFlow.Core.AudioHandling;

namespace StreamFlow.App.Converter;

public class IsDefaultAudioToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value.GetType() == typeof(AudioTrack))
        {
            if ((AudioTrack)value == AudioTrack.Default)
            {
                return false;
            }
        }
        else if (value.GetType() == typeof(SoundEffect))
        {
            if ((SoundEffect)value == SoundEffect.Default)
            {
                return false;
            }
        }

        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
