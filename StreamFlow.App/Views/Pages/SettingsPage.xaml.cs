using System;
using System.Windows;
using System.Windows.Controls;
using System.Runtime.InteropServices;

using StreamFlow.App.Helpers;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.App.Views.Windows;
using StreamFlow.Core.AudioHandling;
using StreamFlow.Core.Data;

using ComboBox = System.Windows.Controls.ComboBox;

namespace StreamFlow.App.Views.Pages;

public partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel
    {
        get;
    }

    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
        AudioEngine.UpdateDevicesInfo();
    }

    private void OutputDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.OutputDeviceSelectionChanged(sender, e);
    }

    private void PlayerWidth_ViewportChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        AppModel.Instance.RequestSave();
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

    private void SettingsCard_Click(object sender, RoutedEventArgs e)
    {

    }
}
