using System.Globalization;
using System.Windows.Data;

namespace StreamFlow.App.Converter;

[ValueConversion(typeof(double), typeof(string))]
/// <summary>Formats a raw width/height ratio as a friendly label ("16:9") for common shapes,
/// falling back to a decimal ratio ("2.39:1") for anything else (e.g. ultrawide monitors).</summary>
public sealed class AspectRatioTextConverter : IValueConverter
{
    private static readonly (double Ratio, string Label)[] CommonRatios =
    [
        (16.0 / 9.0, "16:9"),
        (4.0 / 3.0, "4:3"),
        (21.0 / 9.0, "21:9"),
        (9.0 / 16.0, "9:16"),
        (1.0, "1:1"),
        (3.0 / 2.0, "3:2"),
    ];

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double ratio || ratio <= 0) return "—";

        foreach (var (r, label) in CommonRatios)
            if (Math.Abs(ratio - r) < 0.02) return label;

        return $"{ratio.ToString("F2", culture)}:1";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
