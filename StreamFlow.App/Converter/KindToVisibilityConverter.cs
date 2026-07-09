using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Data;

using StreamFlow.App.Models.Canvas;
using StreamFlow.App.ViewModels.Pages;

namespace StreamFlow.App.Converter;

public class KindToVisibilityConverter : IValueConverter
{
    private ComposeViewModel? ViewModel { get; } = App.Services.GetService(typeof(ComposeViewModel)) as ComposeViewModel;
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if ((CanvasAudioKind)value == CanvasAudioKind.Clone)
        {
            return Visibility.Hidden;
        }
        else
        {
            return Visibility.Visible;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if ((Visibility)value == Visibility.Visible)
        {
            return true;
        }

        return false;
    }
}
