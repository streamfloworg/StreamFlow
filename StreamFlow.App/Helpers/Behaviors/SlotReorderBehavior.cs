using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using StreamFlow.App.ViewModels.Pages;

namespace StreamFlow.App.Helpers.Behaviors;

using DragDropEffects = System.Windows.DragDropEffects;
using ListBox = System.Windows.Controls.ListBox;

/// <summary>Drag-to-reorder for a ListBox of <see cref="SourceSlot"/>s via a dedicated grip
/// element. Reusable across pages: the reordered collection is read from the ListBox's own
/// ItemsSource (not a hardcoded ViewModel reference); <see cref="OnReorderedProperty"/> lets the
/// host page hook whatever side effects it needs after a reorder (e.g. pushing live config).</summary>
public static class SlotReorderBehavior
{
    public static readonly DependencyProperty IsGripProperty = DependencyProperty.RegisterAttached(
        "IsGrip", typeof(bool), typeof(SlotReorderBehavior), new PropertyMetadata(false, OnIsGripChanged));

    public static readonly DependencyProperty IsDropTargetProperty = DependencyProperty.RegisterAttached(
        "IsDropTarget", typeof(bool), typeof(SlotReorderBehavior), new PropertyMetadata(false, OnIsDropTargetChanged));

    public static readonly DependencyProperty OnReorderedProperty = DependencyProperty.RegisterAttached(
        "OnReordered", typeof(ICommand), typeof(SlotReorderBehavior), new PropertyMetadata(null));

    public static bool GetIsGrip(DependencyObject element) => (bool)element.GetValue(IsGripProperty);
    public static void SetIsGrip(DependencyObject element, bool value) => element.SetValue(IsGripProperty, value);

    public static bool GetIsDropTarget(DependencyObject element) => (bool)element.GetValue(IsDropTargetProperty);
    public static void SetIsDropTarget(DependencyObject element, bool value) => element.SetValue(IsDropTargetProperty, value);

    public static ICommand? GetOnReordered(DependencyObject element) => (ICommand?)element.GetValue(OnReorderedProperty);
    public static void SetOnReordered(DependencyObject element, ICommand? value) => element.SetValue(OnReorderedProperty, value);

    private static void OnIsGripChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement grip) return;

        if ((bool)e.NewValue) grip.PreviewMouseLeftButtonDown += Grip_PreviewMouseLeftButtonDown;
        else grip.PreviewMouseLeftButtonDown -= Grip_PreviewMouseLeftButtonDown;
    }

    private static void OnIsDropTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBoxItem item) return;

        if ((bool)e.NewValue)
        {
            item.PreviewDragOver += ListBoxItem_PreviewDragOver;
            item.Drop += ListBoxItem_Drop;
        }
        else
        {
            item.PreviewDragOver -= ListBoxItem_PreviewDragOver;
            item.Drop -= ListBoxItem_Drop;
        }
    }

    private static void Grip_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement grip || grip.DataContext is not SourceSlot slot) return;

        var item = FindVisualParent<ListBoxItem>(grip);
        if (item is not null)
        {
            DragDrop.DoDragDrop(item, slot, DragDropEffects.Move);
        }
    }

    private static void ListBoxItem_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(SourceSlot)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private static void ListBoxItem_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not ListBoxItem item || item.DataContext is not SourceSlot targetSlot) return;

        var sourceSlot = e.Data.GetData(typeof(SourceSlot)) as SourceSlot;
        if (sourceSlot is null || ReferenceEquals(sourceSlot, targetSlot)) return;

        var listBox = FindVisualParent<ListBox>(item);
        if (listBox?.ItemsSource is not ObservableCollection<SourceSlot> slots) return;

        var oldIndex = slots.IndexOf(sourceSlot);
        var newIndex = slots.IndexOf(targetSlot);
        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex) return;

        slots.Move(oldIndex, newIndex);

        var command = GetOnReordered(listBox);
        if (command?.CanExecute(null) == true) command.Execute(null);
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parentObject = VisualTreeHelper.GetParent(child);
        if (parentObject is null) return null;
        if (parentObject is T parent) return parent;
        return FindVisualParent<T>(parentObject);
    }
}
