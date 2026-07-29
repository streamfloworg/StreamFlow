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
            switch (preset)
            {
                case "light":
                    _viewModel.DuckingThresholdDb = -24;
                    _viewModel.DuckingDepth = 0.30f;
                    _viewModel.DuckingAttackMs = 20;
                    _viewModel.DuckingReleaseMs = 200;
                    _viewModel.DuckingHoldMs = 50;
                    break;
                case "medium":
                    _viewModel.DuckingThresholdDb = -30;
                    _viewModel.DuckingDepth = 0.60f;
                    _viewModel.DuckingAttackMs = 15;
                    _viewModel.DuckingReleaseMs = 300;
                    _viewModel.DuckingHoldMs = 100;
                    break;
                case "aggressive":
                    _viewModel.DuckingThresholdDb = -36;
                    _viewModel.DuckingDepth = 0.90f;
                    _viewModel.DuckingAttackMs = 5;
                    _viewModel.DuckingReleaseMs = 500;
                    _viewModel.DuckingHoldMs = 150;
                    break;
            }
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
