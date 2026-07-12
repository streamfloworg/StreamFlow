using System.ComponentModel;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;



using StreamFlow.App.ViewModels.Pages;
using StreamFlow.App.ViewModels.Pages.Compose;
using StreamFlow.App.Views.Windows;
using StreamFlow.Core.AudioHandling;
using StreamFlow.Core.AudioProperties;
using StreamFlow.Core.Cache;
using StreamFlow.Core.Data;
using StreamFlow.Core.Helpers;

using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace StreamFlow.App.Controls;

public partial class PropertiesEditor : UserControl, INotifyPropertyChanged
{
    private static readonly DependencyProperty AudioProperty = DependencyProperty.Register(nameof(Audio), typeof(Audio), typeof(PropertiesEditor), new PropertyMetadata(default(Audio)));

    public Audio Audio
    {
        get
        {
            return (Audio)GetValue(AudioProperty);
        }
        set
        {
            SetValue(AudioProperty, value);
        }
    }

    public PropertiesEditor()
    {
        InitializeComponent();
        Loaded += PropertiesEditor_Loaded;
    }

    private async void PropertiesEditor_Loaded(object sender, RoutedEventArgs e)
    {
        if (Audio != null && Audio.HasMetadata && Audio.Metadata == null)
        {
            Audio.Metadata = AudioEngine.GetAudioMetadata(Audio.FilePath);
            LoggerService.DebugLog(GetType(), $"{Audio.Metadata}");
        }
    }

    [RelayCommand]
    private static void CloseFlyout(object? param) => MainWindow.Current?.AppModelInstance.RequestSave();

    private void ChooseIcon_Click(object sender, RoutedEventArgs e)
    {
        if (Audio != null)
        {
            using var dialog = new OpenFileDialog();
            dialog.Title = "Choose icon image";
            dialog.Filter = "Image Files|" + FileExtension.GetDialogExtensions(AppModel.Instance.ValidImageExtensions);
            dialog.Multiselect = false;
            var result = dialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                SetSelectedAudioIconFromFile(Audio, dialog.FileName);
            }
        }
        else { return; }
    }

    private void ClearIcon_Click(object sender, RoutedEventArgs e)
    {
        if (Audio != null)
        {
            Audio.ImageSource = null;
        }
        else { return; }
    }

    private void IconBorder_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0 && FileExtension.EndsWith(AppModel.Instance.ValidImageExtensions, files[0]))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                return;
            }
        }
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private bool _hasMetadata;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void IconBorder_Drop(object sender, DragEventArgs e)
    {
        if (Audio != null)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0)
            {
                return;
            }

            var file = files[0];
            if (!FileExtension.EndsWith(AppModel.Instance.ValidImageExtensions, file))
            {
                return;
            }

            SetSelectedAudioIconFromFile(Audio, file);
        }
        else { return; }
    }

    private static void SetSelectedAudioIconFromFile(Audio at, string filePath)
    {
        try
        {
            var bmp = new BitmapImage(new Uri(filePath));
            at!.ImageSource = bmp;
        }
        catch (Exception)
        {
            // ignore
        }
    }

    public async void HotkeyTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {

        if (Audio != null && e.Key != Key.Tab)
        {
            // Don't let the event pass further because we don't want
            // standard textbox shortcuts to work.
            e.Handled = true;

            // Get modifiers and key data
            var modifiers = Keyboard.Modifiers;
            var key = e.Key;

            // When Alt is pressed, SystemKey is used instead
            if (key == Key.System)
            {
                key = e.SystemKey;
            }

            // Pressing delete, backspace or escape without modifiers clears the current value
            if (modifiers == ModifierKeys.None &&
                (key == Key.Delete || key == Key.Back || key == Key.Escape))
            {
                Audio.Hotkey = null;
                return;
            }

            // If no actual key was pressed - return
            if (key == Key.LeftCtrl ||
                key == Key.RightCtrl ||
                key == Key.LeftAlt ||
                key == Key.RightAlt ||
                key == Key.LeftShift ||
                key == Key.RightShift ||
                key == Key.LWin ||
                key == Key.RWin ||
                key == Key.Clear ||
                key == Key.OemClear ||
                key == Key.Apps)
            {
                return;
            }

            var candidate = new Hotkey(key, modifiers);

            // Scenes are a second, independent source of hotkeys now (see
            // HotkeyConflictService/ScenesView.SceneHotkeyTextBoxPreviewKeyDown) — checked
            // together with soundboard clips here rather than each surface only knowing about
            // its own kind.
            var conflicts = App.Services.GetService(typeof(Services.HotkeyConflictService)) as Services.HotkeyConflictService;
            var conflict = conflicts?.FindConflict(candidate, excludingOwner: Audio);
            if (conflict is not null)
            {
                var dlg = App.Services.GetService(typeof(Services.IDialogService)) as Services.IDialogService;
                var proceed = dlg is null || await dlg.ConfirmAsync("Hotkey Conflict",
                    $"{candidate} is already assigned to {conflict}. Assign it here too?\n\nBoth will trigger whenever this combo is pressed.",
                    primaryText: "Assign Anyway", secondaryText: "Cancel");
                if (!proceed) return;
            }

            // Update the value
            Audio.Hotkey = candidate;
        }
    }
}
