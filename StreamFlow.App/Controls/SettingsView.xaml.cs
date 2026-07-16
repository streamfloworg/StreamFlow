using System;
using System.Windows;
using System.Windows.Controls;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.App.Views.Windows;
using StreamFlow.Core.AudioHandling;

namespace StreamFlow.App.Controls;

public partial class SettingsView : System.Windows.Controls.UserControl
{
    public SettingsViewModel ViewModel { get; }

    public SettingsView(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;

        InitializeComponent();
        AudioEngine.UpdateDevicesInfo();
        ViewModel.RefreshStreamDeckSettings();
    }

    private void OutputDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.OutputDeviceSelectionChanged(sender, e);
    }

    private void CbThemeSelection_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cbThemeSelection.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var selected = item.Tag?.ToString() ?? "Default";
        MainWindow.Current?.ApplyChosenTheme(selected);
    }
}
