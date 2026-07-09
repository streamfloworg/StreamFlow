using System;
using System.Globalization;
using System.Windows.Data;
using StreamFlow.Core.AudioProperties;

namespace StreamFlow.App.Converter;

public class AudioTypeToAbbreviationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is AudioTypes audioType)
        {
            return audioType switch
            {
                AudioTypes.AudioTrack => "AT",
                AudioTypes.SoundEffect => "FX",
                _ => "??"
            };
        }
        return "??";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
