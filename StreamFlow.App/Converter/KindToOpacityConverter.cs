using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Data;

using StreamFlow.App.Models.Canvas;
using StreamFlow.App.ViewModels.Pages;

namespace StreamFlow.App.Converter;

public class KindToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if ((CanvasAudioKind)value == CanvasAudioKind.Clone)
        {
            return 0.3d;
        }

        return 1.0d;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var data = value as string;
        if (double.TryParse(data, out double result) && result == 0.4d)
        {
            return CanvasAudioKind.Clone;
        }

        return CanvasAudioKind.AudioTrack;
    }
}
