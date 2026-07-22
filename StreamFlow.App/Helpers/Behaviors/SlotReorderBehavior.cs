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
        if (d is not UIElement item) return;

        bool val = (bool)e.NewValue;
        item.AllowDrop = val;

        if (val)
        {
            item.PreviewDragOver += Item_PreviewDragOver;
            item.Drop += Item_Drop;
        }
        else
        {
            item.PreviewDragOver -= Item_PreviewDragOver;
            item.Drop -= Item_Drop;
        }
    }

    private static void Grip_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement grip || grip.DataContext is not SourceSlot slot) return;

        var item = FindVisualParent<TreeViewItem>(grip) as FrameworkElement
                ?? FindVisualParent<ListBoxItem>(grip);

        if (item is not null)
        {
            DragDrop.DoDragDrop(item, slot, DragDropEffects.Move);
        }
    }

    private static void Item_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(SourceSlot)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private static SceneEditorViewModel? GetSceneEditor(DependencyObject container)
    {
        var dataContext = (container as FrameworkElement)?.DataContext;
        if (dataContext is null) return null;
        
        try
        {
            var prop = dataContext.GetType().GetProperty("SceneEditor");
            if (prop is not null)
            {
                return prop.GetValue(dataContext) as SceneEditorViewModel;
            }
        }
        catch
        {
        }
        return null;
    }

    private static void Item_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not FrameworkElement item || item.DataContext is not SourceSlot targetSlot) return;

        var sourceSlot = e.Data.GetData(typeof(SourceSlot)) as SourceSlot;
        if (sourceSlot is null || ReferenceEquals(sourceSlot, targetSlot)) return;

        var container = FindVisualParent<System.Windows.Controls.TreeView>(item) as DependencyObject
                     ?? FindVisualParent<ListBox>(item);
        if (container is null) return;

        var editor = GetSceneEditor(container);
        if (editor is null) return;

        var position = e.GetPosition(item);
        var height = item.ActualHeight;

        // If dropped in the middle of the item, perform grouping!
        bool isMiddle = position.Y > height * 0.25 && position.Y < height * 0.75;
        if (isMiddle && !sourceSlot.IsPrimary && !targetSlot.IsPrimary && sourceSlot.OverlayKind != StreamFlow.Core.Data.OverlayKind.Group && sourceSlot.OverlayKind != StreamFlow.Core.Data.OverlayKind.Alert)
        {
            if (targetSlot.OverlayKind == StreamFlow.Core.Data.OverlayKind.Group || targetSlot.OverlayKind == StreamFlow.Core.Data.OverlayKind.Alert || targetSlot.ParentGroup is not null)
            {
                editor.AddSlotToGroup(sourceSlot, targetSlot);
            }
            else
            {
                editor.GroupTwoSlots(sourceSlot, targetSlot);
            }

            var command = GetOnReordered(container);
            if (command?.CanExecute(null) == true) command.Execute(null);
            return;
        }

        // Near-edge drop: Move sourceSlot before or after targetSlot in the target's collection!
        editor.RemoveSlotFromGroup(sourceSlot);

        System.Collections.IList targetList;
        if (targetSlot.ParentGroup is not null)
        {
            editor.Slots.Remove(sourceSlot);
            
            var targetParentGroup = targetSlot.ParentGroup;
            if (targetParentGroup.Content is GroupOverlayContent group)
            {
                targetList = group.Children;
            }
            else if (targetParentGroup.Content is AlertOverlayContent alert)
            {
                targetList = alert.Children;
            }
            else
            {
                return;
            }
            sourceSlot.ParentGroup = targetParentGroup;
        }
        else
        {
            targetList = editor.Slots;
        }

        var oldIndex = targetList.IndexOf(sourceSlot);
        var newIndex = targetList.IndexOf(targetSlot);

        if (newIndex >= 0)
        {
            if (oldIndex >= 0)
            {
                if (targetList is ObservableCollection<SourceSlot> observableList)
                {
                    observableList.Move(oldIndex, newIndex);
                }
                else
                {
                    targetList.RemoveAt(oldIndex);
                    targetList.Insert(newIndex, sourceSlot);
                }
            }
            else
            {
                targetList.Insert(newIndex, sourceSlot);
            }
        }

        editor.NotifySlotsReorderedCommand.Execute(null);

        var onReorderCommand = GetOnReordered(container);
        if (onReorderCommand?.CanExecute(null) == true) onReorderCommand.Execute(null);
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parentObject = VisualTreeHelper.GetParent(child);
        if (parentObject is null) return null;
        if (parentObject is T parent) return parent;
        return FindVisualParent<T>(parentObject);
    }
}
