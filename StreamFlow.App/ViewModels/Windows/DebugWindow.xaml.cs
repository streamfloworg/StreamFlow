using System.Diagnostics;

using RichCanvas;

using StreamFlow.App.Views.Windows;

namespace StreamFlow.App.ViewModels.Windows;

/// <summary>
/// Interaction logic for DebugWindow.xaml
/// </summary>
public partial class DebugWindow : Window
{
    private bool selfMoving;
    private bool ownerMoving;
    public DebugViewModel ViewModel { get; }// = App.Services.GetService(typeof(DebugViewModel)) as DebugViewModel;
    public DebugWindow(DebugViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += DebugWindow_Loaded;
        IsVisibleChanged += (_, e) => ViewModel.OnWindowVisibilityChanged((bool)e.NewValue);
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        if (!ownerMoving && !selfMoving)
        {
            selfMoving = true;
            Owner.Top = Top;
            Owner.Left = Left + RenderSize.Width;
            base.OnLocationChanged(e);
            selfMoving = false;
        }
    }

    private void DebugWindow_Loaded(object sender, RoutedEventArgs e)
    {
        selfMoving = true;
        Top = Owner.Top;
        Left = Owner.Left - RenderSize.Width;
        InvalidateVisual();
        Owner.LocationChanged += Owner_LocationChanged;
        selfMoving = false;
    }

    private void Owner_LocationChanged(object? sender, EventArgs e)
    {
        if (!selfMoving && !ownerMoving && sender is MainWindow window)
        {
            ownerMoving = true;
            Top = Owner.Top;
            Left = Owner.Left - RenderSize.Width;
            ownerMoving = false;
        }
    }

    //public void TreeViewItemCollapsed(object sender, RoutedEventArgs e)
    //{
    //    ViewModel.TreeViewItemCollapsed(sender, e);
    //}

    //public void TreeViewItemExpanded(object sender, RoutedEventArgs e)
    //{
    //    ViewModel.TreeViewItemExpanded(sender, e);
    //}
}
