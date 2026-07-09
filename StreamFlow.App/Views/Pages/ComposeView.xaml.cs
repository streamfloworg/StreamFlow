using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using StreamFlow.App.ViewModels.Pages;
using StreamFlow.App.ViewModels.Pages.Compose;

namespace StreamFlow.App.Views.Pages;

using MouseButtonEventArgs = MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

public partial class ComposeView
{
    public ComposeViewModel ViewModel
    {
        get;
    }

    public ComposeView(ComposeViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }

    private void WorkflowCanvas_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var shape = FindShapeDataContext(source);
        if (shape is null)
        {
            return;
        }

        if (ViewModel.SelectTypeCommand.CanExecute(shape))
        {
            ViewModel.SelectTypeCommand.Execute(shape);
        }
        else if (ViewModel.ClearSelectionCommand.CanExecute(null))
        {
            ViewModel.ClearSelectionCommand.Execute(null);
        }
    }

    private static CanvasAudioTypeViewModel? FindShapeDataContext(DependencyObject source)
    {
        DependencyObject? current = source;

        while (current != null)
        {
            if (current is FrameworkElement element && element.DataContext is CanvasAudioTypeViewModel shape)
            {
                return shape;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    /// <summary>
    /// Searches for a visual child of a specified Type within the visual tree of a given parent.
    /// </summary>
    /// <remarks>This method performs a depth-first search of the visual tree, returning the first matching
    /// child it encounters.</remarks>
    /// <typeparam name="T">The Type of the visual child to search for. Must be a <see cref="DependencyObject"/>.</typeparam>
    /// <param name="parent">The parent <see cref="DependencyObject"/> in which to search for the visual child.</param>
    /// <returns>The first visual child of Type <typeparamref name="T"/> found within the visual tree of the specified parent;
    /// otherwise, <see langword="null"/> if no such child exists.</returns>
    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);

        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);

            if (child is T match)
            {
                return match;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
    }

    private void OnSelectionDragging(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && (ViewModel.SelectedTypes.Count > 1 || ViewModel.SelectedType != null))
        {
            if (ViewModel.SelectedType is not null && ViewModel.SelectedTypes.Count == 1)
            {
                ViewModel.SelectedType.IsDragging = true;
                return;
            }
            if (ViewModel.SelectedTypes.Count > 0)
            {
                foreach(var type in ViewModel.SelectedTypes)
                {
                    type.IsDragging = true;
                }
                return;
            }
        }
    }

    private void WorkflowCanvas_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.RemovedItems.Count > 0 && e.RemovedItems is List<CanvasAudioTypeViewModel> previouslySelectedItems)
        {
            foreach (var pItem in previouslySelectedItems)
            {
                pItem.IsSelected = false;
            }
        }
        if (e.AddedItems.Count > 0 && e.AddedItems is List<CanvasAudioTypeViewModel> selectedItems)
        {
            foreach(var sItem in selectedItems)
            {
                sItem.IsSelected = true;
            }
        }
    }

    private void OnDragComplete(object sender, MouseButtonEventArgs e)
    {
        foreach(var type in ViewModel.SelectedTypes)
        {
            type.IsDragging = false;
        }
    }

    private void AddButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        AddMenu.IsOpen = true;
    }

    private void GridButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        GridMenu.IsOpen = true;
    }

    private void Slider_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ViewModel.CanvasZoom = 1.0;
    }

    private void Slider_MouseDoubleClick_1(object sender, MouseButtonEventArgs e)
    {
        ViewModel.GridSize = 96;
    }
}
