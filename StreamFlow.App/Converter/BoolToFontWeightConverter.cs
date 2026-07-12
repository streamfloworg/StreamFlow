using System.Globalization;
using System.Windows.Data;

namespace StreamFlow.App.Converter;

/// <summary>Maps TextStyle.IsBold to a WPF FontWeight for the local canvas preview's TextBlock —
/// the real composited/stream render (OverlayContentRenderer.RenderTextToBgra) reads IsBold
/// directly rather than through this.</summary>
public class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
