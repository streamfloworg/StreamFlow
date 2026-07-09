using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StreamFlow.App.Converter;

/// <summary>
/// Converts null values to Visible and non-null values to Hidden/Collapsed.
/// Opposite behavior of NullVisibilityConverter.
/// </summary>
internal sealed class InvertedNullVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
        {
            return Visibility.Visible;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
