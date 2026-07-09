using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace StreamFlow.App.Converter;

/// <summary>
/// Converts a boolean value to a resource (Geometry) for play/pause icon.
/// True = Pause icon (actively playing), False = Play icon (paused/stopped)
/// </summary>
internal sealed class BooleanToResourceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isActivelyPlaying)
        {
            // When actively playing -> show pause icon
            // When paused or stopped -> show play icon
            return isActivelyPlaying 
                ? App.Current.FindResource("PauseIconData") as Geometry 
                : App.Current.FindResource("PlayIconData") as Geometry;
        }

        // Default to play icon
        return App.Current.FindResource("PlayIconData") as Geometry;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
