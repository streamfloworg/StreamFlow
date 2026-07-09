using System.Globalization;
using System.Windows;
using System.Windows.Data;

using StreamFlow.App.ViewModels.Pages;

namespace StreamFlow.App.Converter;

/// <summary>Visible only for TimerMode.CountDown — hides the Duration field for a CountUp timer,
/// which has no target to configure.</summary>
public sealed class TimerModeToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TimerMode.CountDown ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
