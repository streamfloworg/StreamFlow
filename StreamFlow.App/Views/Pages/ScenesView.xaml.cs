using System.Windows;

using StreamFlow.App.ViewModels.Pages;

namespace StreamFlow.App.Views.Pages;

public partial class ScenesView
{
    public ScenesViewModel ViewModel { get; }

    public ScenesView(ScenesViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }

    private async void ScenesView_Loaded(object sender, RoutedEventArgs e) => await ViewModel.OnNavigatedToAsync();

    private async void ScenesView_Unloaded(object sender, RoutedEventArgs e) => await ViewModel.OnNavigatedFromAsync();

    private void AddLayer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is not null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            btn.ContextMenu.IsOpen = true;
        }
    }
}
