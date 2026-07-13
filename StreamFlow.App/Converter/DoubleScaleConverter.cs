using System.Globalization;
using System.Windows.Data;

namespace StreamFlow.App.Converter;

/// <summary>Multiplies a bound double by a fixed factor passed as ConverterParameter — used to
/// keep the chat overlay's local WPF preview TextBlock in proportion with
/// OverlayContentRenderer.RenderChatToBgra's own font-size scaling (Style.FontSize / 3, so the
/// shared 48pt TextStyle default maps to chat's original 16pt), rather than the two rendering
/// paths silently drifting apart.</summary>
public sealed class DoubleScaleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d && parameter is not null && double.TryParse(parameter.ToString(), NumberStyles.Float, culture, out var factor)
            ? d * factor
            : value ?? 0.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
