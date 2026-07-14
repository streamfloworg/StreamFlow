using System.Windows;
using System.Windows.Input;

using StreamFlow.App.Services;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.Core.AudioProperties;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace StreamFlow.App.Views.Pages;

public partial class ScenesView
{
    private readonly HotkeyConflictService _hotkeyConflicts;
    private readonly IDialogService _dialogs;

    public ScenesViewModel ViewModel { get; }

    public ScenesView(ScenesViewModel viewModel, HotkeyConflictService hotkeyConflicts, IDialogService dialogs)
    {
        ViewModel = viewModel;
        _hotkeyConflicts = hotkeyConflicts;
        _dialogs = dialogs;
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

    private void NewGroupFromSelected_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Parent is System.Windows.Controls.ContextMenu contextMenu && contextMenu.PlacementTarget is System.Windows.Controls.ListBox listBox)
        {
            var selectedSlots = listBox.SelectedItems.Cast<SourceSlot>()
                .Where(s => s.OverlayKind != OverlayKind.Group && !s.IsPrimary)
                .ToList();
            if (selectedSlots.Count > 1)
            {
                ViewModel.SceneEditor.GroupSlots(selectedSlots);
            }
        }
    }

    /// <summary>Captures a key combo for the active scene's SwitchHotkey — mirrors
    /// PropertiesEditor.HotkeyTextBoxPreviewKeyDown's capture logic (same modifier-only-key
    /// filtering, same Delete/Backspace/Escape-clears convention) but additionally runs the
    /// candidate through HotkeyConflictService and asks for confirmation before committing,
    /// since scenes are a second, independent source of hotkeys that clip assignment never had
    /// to consider before this existed.</summary>
    private async void SceneHotkeyTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var scene = ViewModel.SceneEditor.ActiveScene;
        if (scene is null || e.Key == Key.Tab) return;

        e.Handled = true;

        var modifiers = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (modifiers == ModifierKeys.None && (key == Key.Delete || key == Key.Back || key == Key.Escape))
        {
            scene.SwitchHotkey = null;
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
            or Key.Clear or Key.OemClear or Key.Apps)
        {
            return;
        }

        var candidate = new Hotkey(key, modifiers);
        var conflict = _hotkeyConflicts.FindConflict(candidate, excludingOwner: scene);
        if (conflict is not null)
        {
            var proceed = await _dialogs.ConfirmAsync("Hotkey Conflict",
                $"{candidate} is already assigned to {conflict}. Assign it here too?\n\nBoth will trigger whenever this combo is pressed.",
                primaryText: "Assign Anyway", secondaryText: "Cancel");
            if (!proceed) return;
        }

        scene.SwitchHotkey = candidate;
    }

    /// <summary>Mirrors GoLiveView.xaml.cs's identical handler — see its own doc comment.</summary>
    private void ChatScrollViewer_ScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange != 0 && sender is System.Windows.Controls.ScrollViewer scrollViewer)
            scrollViewer.ScrollToEnd();
    }
}
