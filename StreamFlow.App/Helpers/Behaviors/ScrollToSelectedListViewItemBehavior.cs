using System.Windows;
using System.Windows.Controls;
using Microsoft.Xaml.Behaviors;

namespace StreamFlow.App.Helpers.Behaviors;

public class ScrollToSelectedListViewItemBehavior : Behavior<System.Windows.Controls.ListView>
{
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.SelectionChanged += AssociatedObjectOnSelectionChanged;
        AssociatedObject.IsVisibleChanged += AssociatedObjectOnIsVisibleChanged;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.SelectionChanged -= AssociatedObjectOnSelectionChanged;
        AssociatedObject.IsVisibleChanged -= AssociatedObjectOnIsVisibleChanged;
        base.OnDetaching();
    }

    private static void AssociatedObjectOnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        ScrollIntoFirstSelectedItem(sender);
    }

    private static void AssociatedObjectOnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ScrollIntoFirstSelectedItem(sender);
    }

    private static void ScrollIntoFirstSelectedItem(object sender)
    {
        if (sender is not System.Windows.Controls.ListView listView)
            return;
        var selectedItems = listView.SelectedItems;
        if (selectedItems.Count > 0)
            listView.ScrollIntoView(selectedItems[0]);
    }
}
