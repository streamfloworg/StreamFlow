using System.Globalization;
using System.Windows.Data;

using StreamFlow.App.ViewModels.Pages;

namespace StreamFlow.App.Converter;

/// <summary>Maps SourceSlot.TimerMode to/from a ComboBox's SelectedIndex (CountDown=0, CountUp=1)
/// — mirrors RotationDegreesToIndexConverter/SceneTransitionKindToIndexConverter's approach for
/// the same "enum via ComboBox index" binding shape.</summary>
public sealed class TimerModeToIndexConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TimerMode mode ? (int)mode : 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int index ? (TimerMode)index : TimerMode.CountDown;
}
