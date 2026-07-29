using System.Windows;
using WpfButton = System.Windows.Controls.Button;
using StreamFlow.App.ViewModels.Pages;

namespace StreamFlow.App.Views.Windows;

public partial class DuckingConfigWindow : Window
{
    private readonly GoLiveViewModel _viewModel;
    private bool _isUpdatingPreset;

    // All four preset buttons in order — used by SetActivePreset to swap styles.
    private WpfButton[] PresetButtons => [PresetLight, PresetMedium, PresetAggressive, PresetCustom];

    public DuckingConfigWindow(GoLiveViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        SetActivePreset(_viewModel.DuckingPreset);
    }

    private void SetActivePreset(string activeTag)
    {
        var active   = (Style)FindResource("SegmentButtonActiveStyle");
        var inactive = (Style)FindResource("SegmentButtonStyle");
        foreach (WpfButton btn in PresetButtons)
            btn.Style = btn.Tag as string == activeTag ? active : inactive;
        _viewModel.DuckingPreset = activeTag;
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: string preset })
        {
            _isUpdatingPreset = true;
            // Delegates to GoLiveViewModel.ApplyDuckingPreset — the single source of truth for
            // preset values (see its own doc comment) — instead of duplicating them here, and
            // (unlike the values this used to hardcode inline) that method also sets
            // IsDuckingEnabled = true, which was previously never set from any UI at all.
            _viewModel.ApplyDuckingPresetCommand.Execute(preset);
            _isUpdatingPreset = false;
            SetActivePreset(preset);
        }
    }

    private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isUpdatingPreset)
            SetActivePreset("custom");
    }

    private void OnDoneClick(object sender, RoutedEventArgs e) => Close();
}
