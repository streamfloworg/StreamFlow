using System.ComponentModel;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using AdonisUI.Controls;

using System.Windows.Controls;

using Microsoft.Toolkit.Uwp.Notifications;

using StreamFlow.App.ViewModels.Pages;
using StreamFlow.App.ViewModels.Windows;
using StreamFlow.App.Views.Pages;
using StreamFlow.App.Controls;
using StreamFlow.Core.AudioHandling;
using StreamFlow.Core.AudioProperties;
using StreamFlow.Core.Data;
using StreamFlow.Core.Helpers;
using StreamFlow.Core.Helpers.KeyboardListener;

using SystemFonts = System.Windows.SystemFonts;

namespace StreamFlow.App.Views.Windows;
public partial class MainWindow : AdonisWindow, INotifyPropertyChanged, IDisposable
{
    private const string APP_GUID = "3A09A246-1D7D-452B-86DE-6AD8AAEC9FBA";

    private readonly KeyboardHook HotKeyHook = new();

    public List<DispatcherUnhandledExceptionEventArgs> StartExceptions { get; set; } = [];

    public AudioViewModel AudioViewModel { get; }

    private readonly SceneEditorViewModel SceneEditor;

    public MainWindowViewModel ViewModel { get; }

    private readonly object audioLock = new();

    public string CurrentPage { get; private set; } = "";

    public AppModel AppModelInstance { get; private set; } = AppModel.Instance;

    public static MainWindow? Current { get; private set; }

    public CommandBinding? PlayPauseCommand { get; private set; }

    public CommandBinding? StopCommand { get; private set; }

    private double _menuFontSize = SystemFonts.MenuFontSize; // Default to system message font size

    public double MenuFontSize
    {
        get
        {
            if (_menuFontSize.Equals(double.NaN))
            {
                return 12d;
            }
            return _menuFontSize;
        }
        private set
        {
            if (_menuFontSize != value)
            {
                _menuFontSize = value;
                OnPropertyChanged(nameof(MenuFontSize));
            }
        }
    }

    private double _iconFontSize = SystemFonts.IconFontSize; // Default to system message font size

    public double IconFontSize
    {
        get
        {
            if (_iconFontSize.Equals(double.NaN))
            {
                return 12d;
            }
            return _iconFontSize;
        }
        private set
        {
            if (_iconFontSize != value)
            {
                _iconFontSize = value;
                OnPropertyChanged(nameof(IconFontSize));
            }
        }
    }

    // Add the INotifyPropertyChanged implementation
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(Current, new PropertyChangedEventArgs(propertyName));
    }

    public MainWindow(
        MainWindowViewModel viewModel,
        AudioViewModel aViewModel,
        SceneEditorViewModel sceneEditor
    )
    {
        ViewModel = viewModel;
        AudioViewModel = aViewModel;
        SceneEditor = sceneEditor;
        Current = this;
        // Theme initialization done via ApplyChosenTheme
        // Subscribe to ApplicationSettings changes for UI updates
        AppModelInstance.Settings.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ApplicationSettings.OutputDevice))
            {
                // Now this will work - notify that AppModelInstance property changed
                Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(AppModelInstance)));
            }
        };
        ApplyChosenTheme(AppModel.Instance.Settings.PreferredTheme);

        // Configure theme watching based on user preference
        InitializeComponent();
        Services.WindowPlacementService.Restore(this);
        // AudioItem has IsSelected="True" in XAML which fires NavMenu_SelectionChanged
        // during InitializeComponent() before ContentFrame is available in the document.
        // Navigate directly in Window.Loaded when the full visual tree is ready.
        Loaded += (_, _) =>
        {
            NavMenu.SelectedItem = NavMenu.Items[0];
            // if (audioView is not null)
            //     Navigate(audioView);
        };
        // Ensure static reference points to the active window instance
        HotKeyManager.RegisterHotKey(new Hotkey(Key.MediaPlayPause, ModifierKeys.None));
        HotKeyHook.OnKeyboard += HotKeyHook_OnKeyboard;

        // Set up follow-system watcher toggle
        AppModel.Instance.Settings.PropertyChanged += Options_PropertyChanged;
        // Theme resources call removed
        BindingOperations.EnableCollectionSynchronization(AudioViewModel.AudioListCollectionView, audioLock);
    }

    private async void HandleMediaKeys(object sender, Core.Helpers.KeyboardListener.KeyboardEventArgs e)
    {
        if (e.Key == Key.MediaPlayPause)
        {
            PlayPauseExecuted(sender);
            e.Handled = true;
        }
        if (e.Key == Key.MediaStop)
        {
            StopExecuted(sender);
            e.Handled = true;
        }
        if (e.Key == Key.VolumeUp)
        {
            VolumeIncreased(sender);
            e.Handled = true;
        }
        if (e.Key == Key.VolumeDown)
        {
            VolumeDecreased(sender);
            e.Handled = true;
        }
        if (e.Key == Key.VolumeMute)
        {
            VolumeMuted(sender);
            e.Handled = true;
        }
    }

    private async void HotKeyHook_OnKeyboard(object sender, Core.Helpers.KeyboardListener.KeyboardEventArgs e)
    {
        if (e.KeyState != KeyState.WM_KEYUP)
            return;

        if (!IsActive)
        {
            HandleMediaKeys(sender, e);
        }

        try
        {
            //LoggerService.DebugLog(GetType(), $"Hot Key Press Detected: {e.Key}");
            foreach (var audio in AppModelInstance.Audios)
            {
                if (audio is not null && audio.Hotkey is not null && audio.Hotkey.Key.Equals(e.Key) && Keyboard.Modifiers.HasFlag(audio.Hotkey.Modifiers))
                {
                    await Current!.AudioViewModel.PlayAudioCommand.ExecuteAsync(audio);
                    e.Handled = true;
                }
            }

            // Scene Switching Hotkeys — same dispatch shape as the audio-clip loop above, just a
            // second independent source of assignable combos (see HotkeyConflictService, which
            // checks both together when either is being assigned).
            foreach (var scene in SceneEditor.Scenes)
            {
                if (scene.SwitchHotkey is not null && scene.SwitchHotkey.Key.Equals(e.Key) && Keyboard.Modifiers.HasFlag(scene.SwitchHotkey.Modifiers))
                {
                    SceneEditor.ActiveScene = scene;
                    e.Handled = true;
                }
            }
        }
        catch (Exception ex)
        {
            LoggerService.ErrorLog(GetType(), $"Error processing hotkey: {ex.Message}");
            LoggerService.ErrorLog(GetType(), $"Stack Trace: {ex.StackTrace}");
            LoggerService.ErrorLog(GetType(), $"Source: {ex.Source}");
        }

        if (AppModelInstance.Settings.StopAllAudioHotKey is not null && e.Key.Equals(AppModelInstance.Settings.StopAllAudioHotKey.Key) && Keyboard.Modifiers.HasFlag(AppModelInstance.Settings.StopAllAudioHotKey.Modifiers))
        {
            Current!.AudioViewModel.StopAudioCommand.Execute(null);
            e.Handled = true;
        }
    }

    public bool Navigate(Page pageType) {
        if (ContentFrame != null)
        {
            return ContentFrame.Navigate(pageType);
        }
        else
            return false;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Immediately stop the TrackPlayer and clear out any audio components/device components to avoid sound remaining active
        try
        {
            TrackPlayer.Stop();
            if (AudioEngine.PlayerInitialized())
            {
                AudioEngine.RemovePlayer();
            }
        }
        catch (Exception ex)
        {
            LoggerService.ErrorLog(GetType(), $"Error stopping audio on window closing: {ex.Message}");
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// Raises the closed event.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        Services.WindowPlacementService.Save(this);
        base.OnClosed(e);
        // Make sure that closing this window will begin the process of closing the application.
        System.Windows.Application.Current.Shutdown();
    }

    public static event EventHandler CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    private void Options_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApplicationSettings.PreferredTheme))
        {
            // Debounce persistence to avoid frequent writes
            AppModel.Instance.RequestSave();
        }

    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        var current = start;
        while (current != null)
        {
            if (current is T typed)
            {
                return typed;
            }

            var parent = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
            current = parent;
        }
        return null;
    }

    private static T? FindDescendant<T>(DependencyObject? start) where T : DependencyObject
    {
        if (start == null)
        {
            return null;
        }

        var count = VisualTreeHelper.GetChildrenCount(start);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(start, i);
            if (child is T t)
            {
                return t;
            }

            var result = FindDescendant<T>(child);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    /// <summary>
    /// Occurs when an exception is thrown by an application but not handled.
    /// </summary>
    public void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        //ShowInfoBar("Unhandled Exception", e.Exception.Message, InfoBarSeverity.Error);
        ShowNotification("Unhandled Exception", $"{e.Exception.Message} - {e.Exception.Source} - {e.Exception.StackTrace}", InfoBarSeverity.Error);
        // For more info see https://docs.microsoft.com/en-us/dotnet/api/system.windows.application.dispatcherunhandledexception?view=windowsdesktop-6.0
    }

    public static void ShowNotification(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        var builder = new ToastContentBuilder()
            .SetToastDuration(ToastDuration.Long)
            .AddToastActivationInfo(string.Empty, ToastActivationType.Foreground);

        switch (severity)
        {
            case InfoBarSeverity.Success:
                builder.AddText("Successful");
                break;
            case InfoBarSeverity.Warning:
                builder.AddText("Warning");
                break;
            case InfoBarSeverity.Error:
                builder.AddText("Error");
                break;
            case InfoBarSeverity.Informational:
            default:
                break;
        }

        builder.AddText(message).AddHeader(APP_GUID, title, "").Show();
    }

    private void Window_Activated(object sender, EventArgs e)
    {
        if (StartExceptions.Count != 0)
        {
            StartExceptions.ForEach(exc =>
            {
                OnDispatcherUnhandledException(Current, exc);
            });
        }
    }

    private readonly GoLiveView? goLiveView = App.Services.GetService(typeof(GoLiveView)) as GoLiveView;
    private readonly AudioView? audioView = App.Services.GetService(typeof(AudioView)) as AudioView;
    private readonly ScenesView? scenesView = App.Services.GetService(typeof(ScenesView)) as ScenesView;
    private readonly ComposeView? composeView = App.Services.GetService(typeof(ComposeView)) as ComposeView;

    private void NavMenu_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavMenu == null || NavMenu.SelectedItem is not ListBoxItem navItem) return;

        // Deselect footer if main is selected
        if (NavMenuFooter != null && NavMenuFooter.SelectedItem != null)
        {
            NavMenuFooter.SelectionChanged -= NavMenuFooter_SelectionChanged;
            NavMenuFooter.SelectedItem = null;
            NavMenuFooter.SelectionChanged += NavMenuFooter_SelectionChanged;
        }

        ViewModel.SearchVisible = false;
        switch (navItem.Name)
        {
            case "GoLiveItem":
                if (goLiveView is null) return;
                Navigate(goLiveView);
                break;
            case "AudioItem":
                ViewModel.SearchVisible = true;
                if (audioView is null) return;
                Navigate(audioView);
                break;
            case "ScenesItem":
                if (scenesView is null) return;
                Navigate(scenesView);
                break;
            case "ComposeItem":
                if (composeView is null) return;
                Navigate(composeView);
                break;
        }

        if (navItem.Content is StackPanel sp && sp.Children.Count > 1 && sp.Children[1] is TextBlock tb)
        {
            CurrentPage = tb.Text;
        }
        else
        {
            CurrentPage = navItem.Content as string ?? "";
        }
        OnPropertyChanged(nameof(CurrentPage));
    }

    private void NavMenuFooter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavMenuFooter == null || NavMenuFooter.SelectedItem is not ListBoxItem navItem) return;

        // Deselect main if footer is selected (except if it is SettingsItem, which navigates)
        if (navItem.Name == "SettingsItem")
        {
            // Reset footer selection so it acts like a button trigger
            NavMenuFooter.SelectionChanged -= NavMenuFooter_SelectionChanged;
            NavMenuFooter.SelectedItem = null;
            NavMenuFooter.SelectionChanged += NavMenuFooter_SelectionChanged;

            ViewModel.SearchVisible = false;
            var settingsVm = App.Services.GetService(typeof(SettingsViewModel)) as SettingsViewModel;
            if (settingsVm is null) return;
            var settingsView = new SettingsView(settingsVm);
            var dialog = new SimpleDialog(settingsView, settingsView.SettingsCloseButton)
            {
                Title = "Settings",
                MinHeight = 490,
                MaxHeight = 490,
                MinWidth = 470,
                MaxWidth = 470,
                Padding = new Thickness(0)
            };
            _ = dialog.ShowAsync();
        }
        else
        {
            // Reset footer selection so it acts like a button trigger
            NavMenuFooter.SelectedItem = null;
        }

        switch (navItem.Name)
        {
#if DEBUG
            case "DebugWindowItem":
                ViewModel.OpenDebugWindowCommand.Execute(null);
                break;
#endif
        }
    }

    [System.Runtime.InteropServices.DllImport("UXTheme.dll", SetLastError = true, EntryPoint = "#138")]
    private static extern bool ShouldSystemUseDarkMode();

    public void ApplyChosenTheme(string themeName)
    {
        var isDark = themeName == "Dark" || (themeName == "Default" && ShouldSystemUseDarkMode());

        var newTheme = isDark
            ? new System.Windows.ResourceDictionary { Source = new Uri("pack://application:,,,/AdonisUI;component/ColorSchemes/Dark.xaml") }
            : new System.Windows.ResourceDictionary { Source = new Uri("pack://application:,,,/AdonisUI;component/ColorSchemes/Light.xaml") };

        // Find the existing color scheme in MergedDictionaries
        var existingTheme = System.Windows.Application.Current.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source != null && d.Source.ToString().Contains("ColorSchemes"));

        if (existingTheme != null)
        {
            int index = System.Windows.Application.Current.Resources.MergedDictionaries.IndexOf(existingTheme);
            System.Windows.Application.Current.Resources.MergedDictionaries.RemoveAt(index);
            System.Windows.Application.Current.Resources.MergedDictionaries.Insert(index, newTheme);
        }
        else
        {
            System.Windows.Application.Current.Resources.MergedDictionaries.Add(newTheme);
        }
    }

    private void CanDecreaseVolume(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = AudioViewModel.IsPlaying && AudioViewModel.CurrentVolume > 0;
        e.Handled = true;
    }

    private void CanIncreaseVolume(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = AudioViewModel.IsPlaying && AudioViewModel.CurrentVolume < 1;
        e.Handled = true;
    }

    private void CanMuteVolume(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = AudioViewModel.IsPlaying;
        e.Handled = true;
    }

    private void PlayPauseCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = AudioViewModel.IsPlaying;
        e.Handled = true;
    }

    private void VolumeIncreased(object sender, ExecutedRoutedEventArgs? e = null)
    {
        LoggerService.DebugLog(GetType(), $"{sender.GetType().Name} called Increase Volume");
        var param = e?.Parameter ?? null;
        AudioViewModel.VolumeIncreaseCommand.Execute(param);
        if (e != null)
        {
            e.Handled = true;
        }
    }

    private void VolumeDecreased(object sender, ExecutedRoutedEventArgs? e = null)
    {
        LoggerService.DebugLog(GetType(), $"{sender.GetType().Name} called Decrease Volume");
        AudioViewModel.VolumeDecreaseCommand.Execute(null);
        if (e != null)
        {
            e.Handled = true;
        }
    }

    private void VolumeMuted(object sender, ExecutedRoutedEventArgs? e = null)
    {
        LoggerService.DebugLog(GetType(), $"{sender.GetType().Name} called Mute/Unmute Volume");
        var param = e?.Parameter ?? null;
        AudioViewModel.VolumeMuteCommand.Execute(param);
        if (e != null)
        {
            e.Handled = true;
        }
    }

    private async void PlayPauseExecuted(object sender, ExecutedRoutedEventArgs? e = null)
    {
        LoggerService.DebugLog(GetType(), $"{sender.GetType().Name} called PlayPauseCommand");
        var param = e?.Parameter ?? null;
        await AudioViewModel.PlayAudioCommand.ExecuteAsync(param);
        if (e != null)
        {
            e.Handled = true;
        }
    }

    private void StopCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = AudioViewModel.IsPlaying;
        e.Handled = true;
    }

    private void StopExecuted(object sender, ExecutedRoutedEventArgs? e = null)
    {
        LoggerService.DebugLog(GetType(), $"{sender.GetType().Name} called StopAudioCommand");
        var param = e?.Parameter ?? null;
        AudioViewModel.StopAudioCommand.Execute(param);
        if (e != null)
        {
            e.Handled = true;
        }
    }

    private void CanExecutePlayNewAudio(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = true;
        e.Handled = true;
    }

    private void PlayNewAudioExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        LoggerService.DebugLog(GetType(), $"{sender.GetType().Name} called PlayNewAudioCommand");
        e.Handled = true;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        HotKeyHook.Dispose();
        AudioEngine.AudioTrackPlayer?.Stop();
        AudioEngine.SoundEffectPlayer?.Stop();
        AudioEngine.Instance.CurrentPlaybackDevice?.Stop();
        AudioEngine.Instance.CurrentPlaybackDevice?.MasterMixer.Components.ToList().ForEach(AudioEngine.Instance.CurrentPlaybackDevice.MasterMixer.RemoveComponent);
        AudioEngine.AudioTrackPlayer?.Dispose();
        AudioEngine.SoundEffectPlayer?.Dispose();
        AudioEngine.Instance.CurrentPlaybackDevice?.Dispose();
    }
}
