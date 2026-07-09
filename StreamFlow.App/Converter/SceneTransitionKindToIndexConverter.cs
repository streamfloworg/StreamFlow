using System.Globalization;
using System.Windows.Data;

using StreamFlow.App.ViewModels.Pages;

namespace StreamFlow.App.Converter;

/// <summary>Maps SceneEditorViewModel.TransitionKind to/from a ComboBox's SelectedIndex, in
/// declaration order (Cut=0, Fade=1, SlideLeft=2, SlideRight=3, SlideUp=4, SlideDown=5) — mirrors
/// RotationDegreesToIndexConverter's approach for the same "enum via ComboBox index" binding
/// shape.</summary>
public sealed class SceneTransitionKindToIndexConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is SceneTransitionKind kind ? (int)kind : 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int index ? (SceneTransitionKind)index : SceneTransitionKind.Cut;
}
