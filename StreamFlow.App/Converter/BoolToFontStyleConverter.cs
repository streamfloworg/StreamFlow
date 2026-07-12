using System.Globalization;
using System.Windows.Data;

namespace StreamFlow.App.Converter;

/// <summary>Maps TextStyle.IsItalic to a WPF FontStyle for the local canvas preview's TextBlock —
/// mirrors BoolToFontWeightConverter's approach for the same "bool to WPF font property" shape.</summary>
public class BoolToFontStyleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
