using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace StreamFlow.App.Converter;


    [ValueConversion(typeof(object), typeof(string))]
    public class TimeCodeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            TimeSpan time;

            if (value is TimeSpan ts)
            {
                time = ts;
            }
            else if (value is double msDouble)
            {
                time = TimeSpan.FromMilliseconds(double.IsNaN(msDouble) || double.IsInfinity(msDouble) ? 0 : msDouble);
            }
            else if (value is long msLong)
            {
                time = TimeSpan.FromMilliseconds(msLong);
            }
            else if (value is int msInt)
            {
                time = TimeSpan.FromMilliseconds(msInt);
            }
            else
            {
                return "00:00.000";
            }

            // Clamps hours into the minute column for standard DAW project tracking
            int totalMinutes = (int)Math.Floor(time.TotalMinutes);

            // Format: 00:00.000 (Minutes:Seconds.Milliseconds)
            return $"{totalMinutes:D2}:{time.Seconds:D2}.{time.Milliseconds:D3}";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Optional: Allows typing a string directly into a text box to jump to a timestamp
            if (value is string input && TimeSpan.TryParseExact(input, @"mm\:ss\.fff", culture, out TimeSpan result))
            {
                return result;
            }
            return TimeSpan.Zero;
        }
    }
