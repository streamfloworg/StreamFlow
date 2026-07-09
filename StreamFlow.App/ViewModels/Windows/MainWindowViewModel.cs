using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Windows.Media;



using Microsoft.Extensions.DependencyInjection;

using StreamFlow.App.Services.Core;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.App.Views.Windows;
using StreamFlow.Core.AudioHandling;

using Logger = StreamFlow.Core.Helpers.LoggerService;
using System.Diagnostics;
using StreamFlow.Core.Data;

namespace StreamFlow.App.ViewModels.Windows;

[DebuggerDisplay("{_playingSoundEffectsStatus}")]
public partial class MainWindowViewModel : ViewModel
{
    [ObservableProperty]
    private bool searchVisible;

    [ObservableProperty]
    private string _applicationTitle = "StreamFlow";

    [ObservableProperty]
    private string _playingSoundEffectsStatus = string.Empty;

    [ObservableProperty]
    private bool _hasSoundEffectsPlaying = false;

    private readonly System.Windows.Threading.DispatcherTimer _soundEffectUpdateTimer;
    private readonly CoreBridgeService _core = App.Services.GetRequiredService<CoreBridgeService>();
    private readonly GoLiveViewModel _goLive = App.Services.GetRequiredService<GoLiveViewModel>();

    public AudioViewModel AVM { get; } = App.Services.GetRequiredService<AudioViewModel>();

    public static ApplicationSettings AppSettings { get; } = AppModel.Instance.Settings;

    /// <summary>"Active"/"Busy"/"Error"/"Starting…" — derived from CoreBridgeService.State plus
    /// GoLiveViewModel.IsStreaming (Core itself has no concept of "streaming", that's an
    /// application-level state layered on top). Shown in the status bar's Core section, the only
    /// place Core's health is visible outside the Go Live page's own buried diagnostics expander.</summary>
    [ObservableProperty]
    private string _coreStatusText = "Starting…";

    [ObservableProperty]
    private System.Windows.Media.Brush _coreStatusBrush = System.Windows.Media.Brushes.Gray;

    /// <summary>Pre-formatted, fixed-width CPU/RAM/VRAM figures from the periodic
    /// CoreStatsEvent — padded so the *character count* per field never changes as the numbers
    /// fluctuate; paired with a monospace font in XAML so that holds visually too, not just in
    /// character count. Empty until the first CoreStatsEvent arrives (~3s after Core starts).</summary>
    [ObservableProperty]
    private string _coreStatsText = "";

    public static Visibility IsDebug
    {
#if DEBUG
        get { return Visibility.Visible; }
#else
        get { return Visibility.Collapsed; }
#endif
    }

    public MainWindowViewModel()
    {
        // Set up timer for real-time updates
        _soundEffectUpdateTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500) // Update every 500ms
        };
        _soundEffectUpdateTimer.Tick += (s, e) => UpdateSoundEffectStatus();
        _soundEffectUpdateTimer.Start();

        UpdateSoundEffectStatus();

        _core.StateChanged += (_, _) => UpdateCoreStatus();
        _core.EventReceived += OnCoreEventReceived;
        _goLive.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GoLiveViewModel.IsStreaming))
                UpdateCoreStatus();
        };
        UpdateCoreStatus();
    }

    private void OnCoreEventReceived(object? sender, Services.Core.CoreEvent evt)
    {
        if (evt is not Services.Core.CoreStatsEvent stats) return;

        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            // Fixed-width padding (character count never changes as values fluctuate) paired
            // with a monospace FontFamily in XAML so it holds visually too, not just in count.
            var vram = stats.VramUsedMb is float used && stats.VramTotalMb is float total
                ? $"  VRAM {used,5:F0}/{total,5:F0}MB"
                : "";
            CoreStatsText = $"CPU {stats.CpuPercent,5:F1}%  MEM {stats.WorkingSetMb,6:F0}MB{vram}";
        }));
    }

    /// <summary>Derives the status bar's Core indicator from CoreBridgeService.State plus
    /// GoLiveViewModel.IsStreaming — Core itself has no notion of "streaming", that's an
    /// application-level concept layered on top of the plain running/exited process state.</summary>
    private void UpdateCoreStatus()
    {
        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            (CoreStatusText, CoreStatusBrush) = _core.State switch
            {
                CoreState.NotStarted => ("Starting…", System.Windows.Media.Brushes.Gray),
                CoreState.Running when _goLive.IsStreaming => ("Active", System.Windows.Media.Brushes.Orange),
                CoreState.Running => ("Idle", System.Windows.Media.Brushes.LimeGreen),
                _ => ("Error", System.Windows.Media.Brushes.OrangeRed),
            };
        }));
    }

    private void OnAudioStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Immediate update for responsive feedback
        UpdateSoundEffectStatus();
    }

    private void UpdateSoundEffectStatus()
    {
        var playingSoundEffects = AudioEngine.GetPlayingSoundEffects();
        var count = playingSoundEffects.Length;

        HasSoundEffectsPlaying = count > 0;

        if (count == 0)
        {
            PlayingSoundEffectsStatus = "";
        }
        else if (count == 1)
        {
            PlayingSoundEffectsStatus = $"♪ {playingSoundEffects[0]}";
        }
        else
        {
            PlayingSoundEffectsStatus = $"♪ {count} sound effects playing";
        }
    }

#if DEBUG

    private static DebugWindow? debugWindow;

    [RelayCommand]
    private static void OpenDebugWindow()
    {
        debugWindow ??= new DebugWindow(App.Services.GetService(typeof(DebugViewModel)) as DebugViewModel)
        {
            Owner = MainWindow.Current,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowState = WindowState.Normal,
            ResizeMode = ResizeMode.NoResize,
            Topmost = true,
        };
        if (debugWindow.IsVisible)
        {
            debugWindow.Hide();
        }
        else
        {
            debugWindow.Show();
        }
    }
#endif

}
