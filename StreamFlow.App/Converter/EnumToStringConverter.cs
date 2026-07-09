using System.Globalization;
using System.Windows.Data;

namespace StreamFlow.App.Converter;

/// <summary>
/// Converts any enum value to its string representation and back.
/// Supports both named enum values and numeric string values.
/// </summary>
internal sealed class EnumToStringConverter : IValueConverter
{
    /// <summary>
    /// Converts an enum value to its string representation (either name or numeric value).
    /// </summary>
    /// <param name="value">The enum value to convert</param>
    /// <param name="targetType">The target type (not used in this direction)</param>
    /// <param name="parameter">Optional converter parameter (not used)</param>
    /// <param name="culture">The culture to use for conversion</param>
    /// <returns>String representation of the enum value (numeric)</returns>
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return null;

        if (!value.GetType().IsEnum)
            return value?.ToString();

        // Convert enum to its underlying numeric value as string (to match Tag values like "0", "1")
        return System.Convert.ToInt32(value).ToString(culture);
    }

    /// <summary>
    /// Converts a string back to the target enum type.
    /// Supports both named enum values ("Spectrogram") and numeric strings ("0", "1").
    /// </summary>
    /// <param name="value">The string value to convert</param>
    /// <param name="targetType">The target enum type</param>
    /// <param name="parameter">Optional converter parameter (not used)</param>
    /// <param name="culture">The culture to use for conversion</param>
    /// <returns>The enum value</returns>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value), "Value cannot be null for enum conversion");

        if (!targetType.IsEnum)
            throw new ArgumentException($"Target type '{targetType.Name}' must be an enum type", nameof(targetType));

        if (value is not string stringValue)
            throw new ArgumentException("Value must be a string for enum conversion", nameof(value));

        // Try to parse as numeric value first (for Tag values like "0", "1")
        if (int.TryParse(stringValue, NumberStyles.Integer, culture, out int intValue))
        {
            if (Enum.IsDefined(targetType, intValue))
                return Enum.ToObject(targetType, intValue);
        }

        // Fallback: try to parse as enum name (for values like "Spectrogram", "Waveform")
        if (Enum.TryParse(targetType, stringValue, ignoreCase: true, out var enumValue))
        {
            return enumValue;
        }

        throw new ArgumentException($"Cannot convert '{stringValue}' to enum type '{targetType.Name}'", nameof(value));
    }
}
