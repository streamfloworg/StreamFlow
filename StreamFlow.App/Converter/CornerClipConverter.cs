using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace StreamFlow.App.Converter;

/// <summary>Builds a rounded-rect clip geometry from (width, height, cornerRadiusPercent) —
/// radius is a percentage of half the shorter side, matching the core's own corner-radius
/// interpretation so the editor preview matches what's actually composited.
/// Bindings: [0] width in pixels (already percent-converted via a fixed ConverterParameter,
/// since the canvas's reference width never changes), [1] HPercent (raw),
/// [2] CanvasHeight (raw) — height is computed here instead of via a nested binding, since
/// MultiBinding.Bindings can't itself contain a MultiBinding.</summary>
public sealed class CornerClipConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [double width, double hPercent, double canvasHeight, double radiusPercent] || width <= 0 || canvasHeight <= 0)
            return Geometry.Empty;

        var height = hPercent / 100.0 * canvasHeight;
        if (height <= 0) return Geometry.Empty;

        var radius = Math.Clamp(radiusPercent, 0, 100) / 100.0 * Math.Min(width, height) / 2.0;
        return new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
