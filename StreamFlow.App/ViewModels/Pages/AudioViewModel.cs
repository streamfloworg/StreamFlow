using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reactive.Linq;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

// iNKORE usings removed

using libxmpBindings;

using StreamFlow.App.Controls;
using StreamFlow.App.Helpers;
using StreamFlow.App.Services;
using StreamFlow.App.Views.Windows;
using StreamFlow.Core;
using StreamFlow.Core.AudioHandling;
using StreamFlow.Core.AudioProperties;
using StreamFlow.Core.Cache;
using StreamFlow.Core.Data;

using Audio = StreamFlow.Core.AudioHandling.Audio;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
// InfoBar alias removed
using Logger = StreamFlow.Core.Helpers.LoggerService;
using Point = System.Windows.Point;

namespace StreamFlow.App.ViewModels.Pages;

public partial class AudioViewModel : ViewModel
{
    [ObservableProperty]
    private static ICollectionView? _audioListCollectionView;

    public static AppModel AppModel { get; } = AppModel.Instance;

    private System.Windows.Controls.StackPanel? InfoPanel { get; set; }

    [ObservableProperty]
    private AudioViewType _viewType = AudioViewType.GridView;

    [ObservableProperty]
    private int _count = 0;

    [ObservableProperty]
    private double _progressWaveWidth = 300;

    private readonly IDisposable? _trackPlayerSubscription;

    private DispatcherTimer? PositionTimer { get; set; }
    private double _posElapsedMilliseconds;
    private double _durationMilliseconds;

    private DispatcherTimer? InfoBarTimer { get; set; }
    private readonly Dictionary<DateTime, InfoBar> activeInfoBars = [];

    [ObservableProperty]
    private TimeSpan _position;

    [ObservableProperty]
    private TimeSpan _seekPosition;

    [ObservableProperty]
    private TimeSpan _duration;

    [ObservableProperty]
    private PlaybackState _playbackState;

    [ObservableProperty]
    private bool _isDebugging;

    private List<AudioTrack> AudioTrackQueue = [];

    //private StreamFlowVisualizer _analyzer;

    public bool IsPaused => PlaybackState == PlaybackState.Paused;

    public bool IsPlaying => PlaybackState == PlaybackState.Playing || IsPaused;

    public bool IsStopped => (PlaybackState == PlaybackState.Stopped) && (Status == Statuses.PlaybackEnded || Status == Statuses.Reset);

    public bool CanStop => !IsStopped;

    /// <summary>
    /// Indicates if the playback is currently active (Playing state only, not paused)
    /// Used for showing pause icon when actively playing
    /// </summary>
    public bool IsActivelyPlaying => PlaybackState == PlaybackState.Playing;

    /// <summary>
    /// Indicates if the play/pause button should be enabled
    /// Enabled when track is loaded and not stopped
    /// </summary>
    public bool CanPlayPause => TrackLoaded || PlaybackState != PlaybackState.Stopped;

    //[ObservableProperty]
    //private VisualizationDimensions _visualizationDimension;

    [Description("String used to update the Audio Track Total Time")]
    [ObservableProperty]
    private TimeSpan _totalTime = TimeSpan.Zero;

    public string TotalText => TotalTime.ToString("mm\\:ss", CultureInfo.InvariantCulture);

    [Description("String used to update the Audio Track Elapsed Time")]
    [ObservableProperty]
    private TimeSpan _elapsedTime = TimeSpan.Zero;
    public string ElapsedText => ElapsedTime.ToString("mm\\:ss", CultureInfo.InvariantCulture);

    [ObservableProperty]
    private Audio? _selectedAudio;

    public static bool IsDebugBuild
#if DEBUG
      => true;
#else
      => false;
#endif

    private AudioTrack _audioTrack = NullAudio.NullTrack;

    public AudioTrack AudioTrack
    {
        get => _audioTrack;
        set
        {
            var previousTrack = _audioTrack;
            _audioTrack = value;
            OnPropertyChanged(nameof(AudioTrack));

            // Only reload loop points if the track actually changed (not just a reassignment of the same instance)
            if (previousTrack != value)
            {
                LoadLoopPointsFromAudioTrack();
            }
        }
    }

    // Index of the currently selected/edited loop point (-1 means creating new)
    [ObservableProperty]
    private int _currentLoopPointIndex = -1;

    partial void OnCurrentLoopPointIndexChanged(int value)
    {
        // When user selects a different loop point from the dropdown
        if (value >= 0 && value < LoopPoints.Count && AudioTrack != null && AudioTrack != NullAudio.NullTrack)
        {
            var loopPoint = AudioTrack.LoopPoints[value];
            SelectionStart = loopPoint.StartLoopSample.TotalSeconds;
            SelectionEnd = loopPoint.EndLoopSample.TotalSeconds;

            Logger.DebugLog(GetType(),
                $"Selected loop point {value} from dropdown: Start={loopPoint.StartLoopSample:mm\\:ss}, End={loopPoint.EndLoopSample:mm\\:ss}");
        }
    }

    [ObservableProperty]
    private ObservableCollection<LoopPoint> _loopPoints = [];

    [ObservableProperty]
    private Statuses _status = Statuses.Reset;

    [Description("List of Playing Sound Effects")]
    [ObservableProperty]
    private HashSet<string> _playingSoundEffects = [];

    //private LevelMeterAnalyzer? LevelMeterAnalyzer { get; set; }

    //public StreamFlowVisualizer? AudioAnalyzer { get; set; }
    //public SpectrumVisualizer? SpectrumVisualizer { get; set; }

    //public SpectrumAnalyzer? SpectrumAnalyzer { get; set; }

    public bool PositionTimerRunning => PositionTimer?.IsEnabled ?? false;

    // Looping control
    [ObservableProperty]
    private bool _isLoopingEnabled = false;

    // Looping control
    [ObservableProperty]
    private bool _isLoopEnabled = false;

    // Loading indicator for MOD/tracker files
    [ObservableProperty]
    private bool _isLoadingTrack = false;

    [ObservableProperty]
    private string _loadingMessage = "";

    [ObservableProperty]
    private string _audioFilePath = string.Empty;

    private float _playbackSpeed = 1.0f;

    public float PlaybackSpeed
    {
        get
        {
            return _playbackSpeed;
        }
        set
        {
            if (TrackPlayer.PlayerRouter != null)
            {
                TrackPlayer.PlayerRouter.PlaybackSpeed = value;
            }
            _playbackSpeed = value;
            OnPropertyChanged(nameof(PlaybackSpeed));
        }
    }

    private float _panning = 0.6f;

    public float Panning
    {
        get
        {
            return _panning;
        }
        set
        {
            if (TrackPlayer.PlayerRouter != null)
            {
                TrackPlayer.PlayerRouter.Pan = value;
            }
            _panning = value;
            OnPropertyChanged(nameof(Panning));
        }
    }

    partial void OnIsLoopEnabledChanged(bool value)
    {
        // Apply looping to the audio track if it's currently loaded
        if (AudioTrack != null && AudioTrack != NullAudio.NullTrack && HasSelection)
        {
            if (value)
            {
                Logger.DebugLog(GetType(),
                    $"Looping enabled: {SelectionStartTime:mm\\:ss} - {SelectionEndTime:mm\\:ss}");
                // Enable looping with current selection
                ApplyLoopToPlayback();
            }
            else
            {
                Logger.DebugLog(GetType(), "Looping disabled");
                // Disable looping
                DisableLoopPlayback();
            }
        }
    }

    // Selection properties - selection is now triggered by Shift key instead of mode toggle
    [ObservableProperty]
    private double _selectionStart = 0.0;

    [ObservableProperty]
    private double _selectionEnd = 0.0;

    [ObservableProperty]
    private bool _hasSelection = false;

    partial void OnHasSelectionChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSelection));
    }

    public bool ShowSelection => _hasSelection;

    public TimeSpan SelectionStartTime => TimeSpan.FromSeconds(SelectionStart);
    public TimeSpan SelectionEndTime => TimeSpan.FromSeconds(SelectionEnd);
    public TimeSpan SelectionDuration => SelectionEndTime - SelectionStartTime;

    private static Dispatcher? UIDispatcher => MainWindow.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;

    [ObservableProperty]
    private double _rms;

    [ObservableProperty]
    private float _peak;

    [ObservableProperty]
    private double _currentVolume;

    [ObservableProperty]
    private bool _isSeeking;

    private bool _isSwitchingTracks;

    [ObservableProperty]
    private bool _showLevelMeter;

    [RelayCommand]
    private void SetAudioView(object? param)
    {
        switch(ViewType)
        {
            case AudioViewType.GridView:
                ViewType = AudioViewType.ListView;
                break;
            case AudioViewType.ListView:
                ViewType = AudioViewType.GridView;
                break;
            default:
                ViewType = AudioViewType.GridView;
                break;
        }
    }

    private bool wasPaused;

    partial void OnIsSeekingChanged(bool oldValue, bool newValue)
    {
        if (newValue != oldValue)
        {
            if (newValue)
            {
                if (IsPaused && !wasPaused)
                {
                    wasPaused = true;
                }
                else
                {
                    TrackPlayer.Pause();
                }
            }
            else
            {
                if (wasPaused)
                {
                    wasPaused = false;
                }
                else
                {
                    TrackPlayer.Play();
                }
            }
        }
    }

    /// <summary>
    /// Handles the event when the position of the track changes.
    /// </summary>
    /// <remarks>This method updates the track's position if it differs from the current position.</remarks>
    /// <param name="value">The new position of the track as a <see cref="TimeSpan"/>.</param>
    partial void OnSeekPositionChanged(TimeSpan value)
    {
        //Debug.WriteLine($"Position changed to {value}");
        if (value != TrackPlayer.Position && IsSeeking)
        {
            TrackPlayer.Seek(value);
            Logger.DebugLog(GetType(), $"SeekPosition Changed to {value}");
        }
    }

    partial void OnSelectionStartChanged(double value)
    {
        UpdateSelection();
    }

    partial void OnSelectionEndChanged(double value)
    {
        UpdateSelection();
    }

    partial void OnCurrentVolumeChanged(double oldValue, double newValue)
    {
        try
        {
            if (AudioTrack != NullAudio.NullTrack && TrackPlayer.PlayerRouter != null && oldValue != newValue)
            {
                TrackPlayer.PlayerRouter.Volume = (float)newValue;
                AudioTrack.Volume = (float)newValue;
            }
        }
        catch { }
    }

    //partial void OnVisualizationDimensionChanged(VisualizationDimensions value)
    //{
    //    Logger.DebugLog(GetType(), $"VisualizationDimension changed: {value}");
    //}

    private readonly Geometry? MusicIconData = App.Current?.FindResource("MusicIconData") as Geometry;

    public Style? GridViewStyle { get; private set; }

    public Style? ListViewStyle { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioViewModel"/> class.
    /// </summary>
    /// <remarks>This constructor sets up the initial state of the view model, including subscribing to track
    /// player events,  configuring the audio list collection view, and initializing timers for position updates and the
    /// info bar. It also listens for changes in filter options to dynamically update the audio list view.</remarks>
    public AudioViewModel()
    {
        // Initial state sync
        // Subscribe to Track Player Observable Events
        // Allow Loaded status through to ensure UI updates after track loading completes
        _trackPlayerSubscription = TrackPlayer.MultiCast
            .Where(e => e.Status != Statuses.Loading)    // Filter out Loading, but allow Loaded, Ready, etc.
            .Subscribe(OnTrackPlayerChanged, OnTrackPlayerError);                       // Handle events
        AudioListCollectionView = CollectionViewSource.GetDefaultView(AppModel.Instance.Audios);
        AudioListCollectionView.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
        AppModel.Instance.Settings.FilterOptions.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(FilterOptions.SearchTerm))
            {
                AudioListCollectionView.Refresh();
            }
        };

        AudioListCollectionView.Filter += FilterAudio;
        GridViewStyle ??= App.Current?.FindResource("AudioGridView") as Style;
        ListViewStyle ??= App.Current?.FindResource("AudioListView") as Style;
    }

    /// <summary>
    /// Handles the periodic timer tick event to manage the display of active information bars.
    /// </summary>
    /// <remarks>This method processes the collection of active information bars, adding new ones to the
    /// display and removing those that have expired. It ensures that the information bars are updated in the
    /// application's main window, if available. The method operates based on the current time and the scheduled display
    /// times of the information bars.</remarks>
    /// <param name="sender">The source of the event. This parameter is typically ignored in this context.</param>
    /// <param name="e">The event data associated with the timer tick.</param>
    private void InfoBarTimerTick(object? sender, EventArgs e)
    {
        // Process any active infobars - add new and remove expired
        if (activeInfoBars.Count > 0 && MainWindow.Current is not null)
        {
            var now = DateTime.Now;
            var keysToRemove = activeInfoBars.Keys.Where(k => k <= now).ToList();
            var keysToAdd = activeInfoBars.Keys.Where(k => k > now).ToList();

            // Add new
            foreach (var key in keysToAdd)
            {
                if (InfoPanel is not null && activeInfoBars.TryGetValue(key, out var infobar) && !InfoPanel.Children.Contains(infobar))
                {
                    if (infobar is not null)
                    {
                        ShowInfoBar(infobar);
                    }
                }
            }

            // Remove expired
            foreach (var key in keysToRemove)
            {
                if (InfoPanel is not null && activeInfoBars.TryGetValue(key, out var infobar) && InfoPanel.Children.Contains(infobar))
                {
                    HideNowPlaying(infobar);
                    activeInfoBars.Remove(key);
                }
            }
        }
        else
        {
            // No active infobars
            return;
        }
    }

    /// <summary>
    /// Opens a file dialog to allow the user to select one or more audio files and processes the selected files.
    /// </summary>
    /// <remarks>The file dialog is pre-configured to filter for valid audio file extensions as defined by the
    /// application.  If the user selects one or more files, they are processed asynchronously.</remarks>
    /// <returns></returns>
    [RelayCommand]
    private static async Task AddAudio()
    {
        OpenFileDialog FileDialog = new()
        {
            RestoreDirectory = true,
            DefaultExt = FileExtension.GetDialogExtensions(AppModel.Instance.ValidAudioExtensions).Split(",").FirstOrDefault(),
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = true,
            Filter = "Audio Files | " + FileExtension.GetDialogExtensions(AppModel.Instance.ValidAudioExtensions),
            Title = "Select Audio File(s)"
        };
        if (FileDialog.ShowDialog() == DialogResult.OK)
        {
            foreach (var file in FileDialog.FileNames)
            {
                await new FileDragDrop(file).ShowAsync();
            }
        }
    }

    /// <summary>
    /// Tests the XMP library by loading a module file and displaying its format information.
    /// </summary>
    /// <remarks>
    /// This command opens a file dialog to select a module file (.mod, .xm, .s3m, .it, etc.),
    /// loads it using the libxmpBindings library, and displays format information such as
    /// sample rate, channels, bit depth, and estimated duration. This is useful for verifying
    /// that the XMP library integration is working correctly.
    /// </remarks>
    [RelayCommand]
    private async Task TestXmpFormat()
    {
        OpenFileDialog fileDialog = new()
        {
            RestoreDirectory = true,
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
            Filter = "Module Files (*.mod, *.xm, *.s3m, *.it)|*.mod;*.xm;*.s3m;*.it;*.mtm;*.669;*.ult;*.med;*.far;*.stm;*.gdm|All Files|*.*",
            Title = "Select Module File to Test"
        };

        if (fileDialog.ShowDialog() != DialogResult.OK)
            return;

        var result = XmpTestService.TestModuleFormat(fileDialog.FileName);

        var severity = result.Severity switch
        {
            "Success" => InfoBarSeverity.Success,
            "Warning" => InfoBarSeverity.Warning,
            "Error" => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational
        };

        await ShowXmpTestResult(result.Title, result.Message, severity);

        if (result.Success)
        {
            Logger.InfoLog(GetType(), $"XMP Format Test: {result.Title}");
        }
        else
        {
            Logger.ErrorLog(GetType(), $"XMP Format Test failed: {result.Title}");
        }
    }

    /// <summary>
    /// Tests XMP streaming playback by loading a module file and playing it through the audio engine.
    /// </summary>
    [RelayCommand]
    private async Task TestXmpStreaming()
    {
        OpenFileDialog fileDialog = new()
        {
            RestoreDirectory = true,
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
            Filter = "Module Files (*.mod, *.xm, *.s3m, *.it)|*.mod;*.xm;*.s3m;*.it;*.mtm;*.669;*.ult;*.med;*.far;*.stm;*.gdm|All Files|*.*",
            Title = "Select Module File to Play"
        };

        if (fileDialog.ShowDialog() != DialogResult.OK)
            return;

        var (success, player, result, resources) = await XmpTestService.TestModuleStreaming(fileDialog.FileName);

        if (!success)
        {
            var severity = result.Severity switch
            {
                "Success" => InfoBarSeverity.Success,
                "Warning" => InfoBarSeverity.Warning,
                "Error" => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Informational
            };

            await ShowXmpTestResult(result.Title, result.Message, severity);
            Logger.ErrorLog(GetType(), $"XMP Streaming Test failed: {result.Title}");
            return;
        }

        try
        {
            // Create a dialog that stays open during playback using standard AdonisWindow
            var playbackDialog = new AdonisUI.Controls.AdonisWindow
            {
                Title = result.Title,
                Content = new System.Windows.Controls.StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = result.Message,
                            TextWrapping = System.Windows.TextWrapping.Wrap,
                            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                            Margin = new System.Windows.Thickness(12)
                        },
                        new System.Windows.Controls.Button
                        {
                            Content = "Stop Playback",
                            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                            Margin = new System.Windows.Thickness(12)
                        }
                    }
                },
                Width = 450,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = MainWindow.Current
            };

            var stopBtn = (System.Windows.Controls.Button)((System.Windows.Controls.StackPanel)playbackDialog.Content).Children[1];
            stopBtn.Click += (s, e) => playbackDialog.Close();

            // Start playback on background thread
            var playbackTask = Task.Run(() =>
            {
                try
                {
                    player!.Play();
                    Logger.InfoLog(GetType(), $"Started streaming playback test");

                    // Wait for playback to complete or be stopped
                    while (player.State == SoundFlow.Enums.PlaybackState.Playing)
                    {
                        Task.Delay(100).Wait();
                    }
                }
                catch (Exception ex)
                {
                    Logger.ErrorLog(GetType(), $"Playback error: {ex.Message}");
                }
            });

            // Show dialog (blocks until user closes it)
            if (UIDispatcher != null)
            {
                await UIDispatcher.InvokeAsync(() =>
                {
                    playbackDialog.ShowDialog();
                });
            }

            // Cleanup is handled by DisposableResources
            resources?.Dispose();

            Logger.InfoLog(GetType(), "XMP Streaming Test completed");

            // Show completion message
            await ShowXmpTestResult(
                "Streaming Playback Complete",
                "The module was streamed successfully without pre-rendering.",
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            resources?.Dispose();
            await ShowXmpTestResult(
                "XMP Streaming Test Error",
                $"Exception: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                InfoBarSeverity.Error);

            Logger.ErrorLog(GetType(), $"XMP Streaming Test failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task TestXmpNativePlaying()
    {
        OpenFileDialog fileDialog = new()
        {
            RestoreDirectory = true,
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
            Filter = "Module Files (*.mod, *.xm, *.s3m, *.it)|*.mod;*.xm;*.s3m;*.it;*.mtm;*.669;*.ult;*.med;*.far;*.stm;*.gdm|All Files|*.*",
            Title = "Select Module File to Test"
        };

        if (fileDialog.ShowDialog() != DialogResult.OK)
            return;

        var filePath = fileDialog.FileName;

        try
        {
            // Test the module using TestModule first
            if (!Xmp.TestModule(filePath, out var testInfo))
            {
                await ShowXmpTestResult(
                    "Module Test Failed",
                    $"File: {Path.GetFileName(filePath)}\n\nThe file is not recognized as a valid module format.",
                    InfoBarSeverity.Error);
                return;
            }

            // Create XMP instance and load module
            using var xmp = new Xmp(rate: 44100, format: XmpFormat.None);

            if (!xmp.LoadModule(filePath))
            {
                await ShowXmpTestResult(
                    "Module Load Failed",
                    $"File: {Path.GetFileName(filePath)}\n\nFailed to load the module file.",
                    InfoBarSeverity.Error);
                return;
            }

            // Get audio format information
            var formatInfo = xmp.GetAudioFormat();

            if (formatInfo == null)
            {
                await ShowXmpTestResult(
                    "Format Detection Failed",
                    $"File: {Path.GetFileName(filePath)}\n\nCould not retrieve audio format information.",
                    InfoBarSeverity.Warning);
                return;
            }

            // Build detailed info message
            var infoMessage = $"File: {Path.GetFileName(filePath)}\n" +
                            $"Module Name: {testInfo.Name}\n" +
                            $"Format: {testInfo.Format}\n\n" +
                            $"Audio Format:\n" +
                            $"  Sample Rate: {formatInfo.SampleRate} Hz\n" +
                            $"  Channels: {formatInfo.Channels} ({(formatInfo.IsMono ? "Mono" : "Stereo")})\n" +
                            $"  Bit Depth: {formatInfo.BitsPerSample} bit\n" +
                            $"  Format Flags: {formatInfo.Format}\n" +
                            $"  Estimated Duration: {formatInfo.EstimatedDuration:mm\\:ss}\n" +
                            $"  Estimated Total Play time: {xmp.GetEstimatedTotalPlayTime(filePath)}" +
                            $"  Block Align: {formatInfo.BlockAlign} bytes\n" +
                            $"  Avg. Bytes/Sec: {formatInfo.AverageBytesPerSecond:N0}";

            Logger.InfoLog(GetType(), $"XMP Format Test: {testInfo.Name} ({testInfo.Format}) - {formatInfo.SampleRate}Hz, {formatInfo.Channels}ch, {formatInfo.BitsPerSample}bit");

            xmp.LoadModule(filePath);
            xmp.StartPlayer();
        }
        catch (Exception ex)
        {
            await ShowXmpTestResult(
                "XMP Test Error",
                $"File: {Path.GetFileName(filePath)}\n\nException: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                InfoBarSeverity.Error);

            Logger.ErrorLog(GetType(), $"XMP Format Test failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows a content dialog with XMP test results.
    /// </summary>
    private async Task ShowXmpTestResult(string title, string message, InfoBarSeverity severity)
    {
        if (UIDispatcher != null)
        {
            await UIDispatcher.InvokeAsync(() =>
            {
                AdonisUI.Controls.MessageBox.Show(MainWindow.Current, message, title, AdonisUI.Controls.MessageBoxButton.OK);
            });
        }
    }

    /// <summary>
    /// Removes the specified audio item, with optional confirmation based on the current keyboard modifier state.
    /// </summary>
    /// <remarks>If the <see cref="Keyboard.Modifiers"/> property includes the <see
    /// cref="ModifierKeys.Shift"/> key, the audio item is deleted immediately without confirmation. Otherwise, a
    /// confirmation dialog is displayed before deletion.</remarks>
    /// <param name="param">The parameter representing the button that triggered the command. The button's <see cref="Button.DataContext"/>
    /// must be an <see cref="Audio"/> object to identify the audio item to remove.</param>
    [RelayCommand]
    private async Task RemoveAudio(object? param)
    {
        // Mark the routed event as handled to prevent it from bubbling up to ListView

        if (param is not Audio audioToDelete)
        {
            return;
        }
        else if (SelectedAudio is null || audioToDelete != SelectedAudio)
        {
            return;
        }

        try
        {
            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                var ad = new AudioDeletion(audioToDelete);
                ad.DeleteWithoutConfirmation();
            }
            else
            {
                await new AudioDeletion(audioToDelete).ShowAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error removing Audio: {ex.Message}");
        }
    }

    public async void RemoveAudioClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not null)
        {
            await RemoveAudio(((MenuItem)sender).Tag);
        }
    }

    /// <summary>
    /// Updates the selection state and derived properties.
    /// Does NOT commit to LoopPoints - only updates visual state.
    /// </summary>
    private void UpdateSelection()
    {
        HasSelection = SelectionEnd > SelectionStart && SelectionEnd > 0;
        OnPropertyChanged(nameof(SelectionStartTime));
        OnPropertyChanged(nameof(SelectionEndTime));
        OnPropertyChanged(nameof(SelectionDuration));

        if (HasSelection)
        {
            Logger.DebugLog(GetType(),
                $"Selection updated (visual only): {SelectionStartTime:mm\\:ss} to {SelectionEndTime:mm\\:ss} " +
                $"(Duration: {SelectionDuration:mm\\:ss})");
        }
    }

    /// <summary>
    /// Commits the current selection to the AudioTrack's LoopPoints collection.
    /// Should only be called when selection is finalized (mouse up).
    /// </summary>
    [RelayCommand]
    private async Task CommitSelection(bool Cancelled = false)
    {
        if (!HasSelection || Cancelled) return;
        if (AudioTrack == null || AudioTrack == NullAudio.NullTrack) return;

        // Check if we're updating an existing loop point or creating a new one
        var isUpdatingExisting = CurrentLoopPointIndex >= 0 && CurrentLoopPointIndex < AudioTrack.LoopPoints.Count;

        string loopPointName;
        bool cancelled;

        if (isUpdatingExisting)
        {
            // Keep the existing name when modifying times
            loopPointName = AudioTrack.LoopPoints[CurrentLoopPointIndex].Name;

            // Store the current index to restore selection after update
            var selectedIndex = CurrentLoopPointIndex;

            // Update existing loop point times
            var loopPoint = new LoopPoint(SelectionStartTime, SelectionEndTime, loopPointName);
            AudioTrack.LoopPoints[selectedIndex] = loopPoint;
            LoopPoints[selectedIndex] = loopPoint;

            // Force ComboBox to refresh by notifying the collection changed
            OnPropertyChanged(nameof(LoopPoints));

            // Restore selection - this ensures the ComboBox stays on the same item
            CurrentLoopPointIndex = selectedIndex;

            Logger.DebugLog(GetType(),
                $"Updated loop point {selectedIndex} '{loopPointName}': Start={loopPoint.StartLoopSample:mm\\:ss}, End={loopPoint.EndLoopSample:mm\\:ss}");
        }
        else
        {
            // Prompt for name when creating a new loop point
            (loopPointName, cancelled) = await ShowLoopPointNameDialog(CurrentLoopPointIndex);
            if (loopPointName == null || !cancelled) return; // User cancelled

            var loopPoint = new LoopPoint(SelectionStartTime, SelectionEndTime, loopPointName);

            // Add new loop point to AudioTrack
            AudioTrack.LoopPoints.Add(loopPoint);

            // Add to ObservableCollection (this will automatically update the UI)
            LoopPoints.Add(loopPoint);

            // Set the newly added item as selected
            CurrentLoopPointIndex = AudioTrack.LoopPoints.Count - 1;

            Logger.DebugLog(GetType(),
                $"Added new loop point {CurrentLoopPointIndex} '{loopPointName}': Start={loopPoint.StartLoopSample:mm\\:ss}, End={loopPoint.EndLoopSample:mm\\:ss}");
        }
    }

    /// <summary>
    /// Shows a dialog to prompt the user for a loop point name
    /// </summary>
    /// <param name="loopPointIndex">The index of the loop point being edited, or -1 for a new loop point</param>
    /// <returns>The name entered by the user, or null if cancelled</returns>
    private async Task<(string?,bool)> ShowLoopPointNameDialog(int loopPointIndex)
    {
        var isNewLoopPoint = loopPointIndex < 0 || loopPointIndex >= AudioTrack.LoopPoints.Count;
        var existingName = !isNewLoopPoint ? AudioTrack.LoopPoints[loopPointIndex].Name : string.Empty;
        var displayIndex = isNewLoopPoint ? AudioTrack.LoopPoints.Count + 1 : loopPointIndex + 1;

        var dialog = new LoopPointNameDialog(existingName, displayIndex);
        await dialog.ShowAsync();
        return await dialog.WaitForResultAsync();
    }

    /// <summary>
    /// Loads loop points from the current AudioTrack and displays all of them
    /// </summary>
    private void LoadLoopPointsFromAudioTrack()
    {
        if (AudioTrack == null || AudioTrack == NullAudio.NullTrack || AudioTrack.LoopPoints == null || AudioTrack.LoopPoints.Count == 0)
        {
            // No loop points to load, clear selection
            SelectionStart = 0;
            SelectionEnd = 0;
            CurrentLoopPointIndex = -1;
            return;
        }
        UIDispatcher?.InvokeAsync(async () =>
        {
            // Load all loop points into ObservableCollection
            if (LoopPoints.Any())
            {
                LoopPoints.Clear();
            }
            foreach (var loopPoint in AudioTrack.LoopPoints)
            {
                LoopPoints.Add(loopPoint);
            }
        });
        // Select the first loop point for display
        if (AudioTrack.LoopPoints.Count > 0)
        {
            CurrentLoopPointIndex = 0;
            var loopPoint = AudioTrack.LoopPoints[0];
            SelectionStart = loopPoint.StartLoopSample.TotalSeconds;
            SelectionEnd = loopPoint.EndLoopSample.TotalSeconds;

            Logger.DebugLog(GetType(),
                $"Loaded {AudioTrack.LoopPoints.Count} loop point(s) from AudioTrack. Displaying first: Start={loopPoint.StartLoopSample:mm\\:ss}, End={loopPoint.EndLoopSample:mm\\:ss}");
        }
    }

    /// <summary>
    /// Starts creating a new loop point selection
    /// </summary>
    [RelayCommand]
    private void StartNewSelection()
    {
        CurrentLoopPointIndex = AudioTrack.LoopPoints.Count > 0 ? AudioTrack.LoopPoints.Count - 1 : 0;
        SelectionStart = 0;
        SelectionEnd = 0;
        SelectionStart = Position.TotalSeconds;
        SelectionEnd = Position.TotalSeconds + 30 > Duration.TotalSeconds ? Duration.TotalSeconds : Position.TotalSeconds + 30;
        Logger.DebugLog(GetType(), "Started new selection mode");
    }

    /// <summary>
    /// Deletes a loop point at the specified index
    /// </summary>
    [RelayCommand]
    private void DeleteLoopPoint(int index)
    {
        if (AudioTrack == null || AudioTrack == NullAudio.NullTrack) return;
        if (index < 0 || index >= AudioTrack.LoopPoints.Count) return;

        var loopPoint = AudioTrack.LoopPoints[index];

        // Remove from AudioTrack
        AudioTrack.LoopPoints.RemoveAt(index);

        // Remove from ObservableCollection (this will automatically update the UI)
        LoopPoints.RemoveAt(index);

        // If we deleted the current selection, clear it
        if (CurrentLoopPointIndex == index)
        {
            SelectionStart = 0;
            SelectionEnd = 0;
            CurrentLoopPointIndex = -1;
        }
        else if (CurrentLoopPointIndex > index)
        {
            // Adjust the index if we deleted something before it
            CurrentLoopPointIndex--;
        }

        Logger.DebugLog(GetType(),
            $"Deleted loop point {index}: Start={loopPoint.StartLoopSample:mm\\:ss}, End={loopPoint.EndLoopSample:mm\\:ss}");
    }

    /// <summary>
    /// Edits the name of a loop point at the specified index
    /// </summary>
    public async Task EditLoopPointName(int index)
    {
        if (AudioTrack == null || AudioTrack == NullAudio.NullTrack) return;
        if (index < 0 || index >= AudioTrack.LoopPoints.Count) return;

        var loopPoint = AudioTrack.LoopPoints[index];
        var (newName, cancelled) = await ShowLoopPointNameDialog(index);

        if (!cancelled)
        {
            if (newName != null && newName != loopPoint.Name)
            {
                loopPoint.Name = newName;

                // Update both collections
                AudioTrack.LoopPoints[index] = loopPoint;
                LoopPoints[index] = loopPoint;

                // Force ComboBox to refresh by notifying the collection changed
                OnPropertyChanged(nameof(LoopPoints));

                // Restore selection to keep the same item selected in the ComboBox
                CurrentLoopPointIndex = index;

                Logger.DebugLog(GetType(),
                    $"Renamed loop point {index} to '{newName}': Start={loopPoint.StartLoopSample:mm\\:ss}, End={loopPoint.EndLoopSample:mm\\:ss}");
            }
        }
        else
        {

        }
    }

    /// <summary>
    /// Selects a loop point for editing by its index
    /// </summary>
    [RelayCommand]
    private void SelectLoopPoint(int index)
    {
        if (AudioTrack == null || AudioTrack == NullAudio.NullTrack) return;
        if (index < 0 || index >= AudioTrack.LoopPoints.Count) return;

        CurrentLoopPointIndex = index;
        var loopPoint = AudioTrack.LoopPoints[index];
        SelectionStart = loopPoint.StartLoopSample.TotalSeconds;
        SelectionEnd = loopPoint.EndLoopSample.TotalSeconds;

        Logger.DebugLog(GetType(),
            $"Selected loop point {index} for editing: Start={loopPoint.StartLoopSample:mm\\:ss}, End={loopPoint.EndLoopSample:mm\\:ss}");
    }

    /// <summary>
    /// Clears the current selection range.
    /// </summary>
    [RelayCommand]
    private void ClearSelection()
    {
        SelectionStart = 0.0;
        SelectionEnd = 0.0;
        UpdateSelection();

        // Disable looping if it was enabled
        if (IsLoopEnabled)
        {
            IsLoopEnabled = false;
        }

        Logger.DebugLog(GetType(), "Selection cleared");
    }

    /// <summary>
    /// Applies looping to the current playback based on the selected loop point
    /// </summary>
    private void ApplyLoopToPlayback()
    {
        if (!HasSelection || AudioTrack == null || AudioTrack == NullAudio.NullTrack)
            return;

        // The actual looping logic is handled in UpdateHeader()
        // This method is called when looping is first enabled

        // If playback position is outside the loop range, seek to loop start
        if (Position < SelectionStartTime || Position >= SelectionEndTime)
        {
            TrackPlayer.Seek(SelectionStartTime);
            SeekPosition = SelectionStartTime;
            UpdateHeader();
            Logger.DebugLog(GetType(),
                $"Loop enabled: Seeking from {Position:mm\\:ss} to loop start {SelectionStartTime:mm\\:ss}");
        }

        Logger.DebugLog(GetType(),
            $"Applied loop range: {SelectionStartTime:mm\\:ss} to {SelectionEndTime:mm\\:ss}");

        SeekPosition = TimeSpan.MinValue;
    }

    /// <summary>
    /// Disables looping and returns to normal playback
    /// </summary>
    private void DisableLoopPlayback()
    {
        // The looping check in UpdateHeader() will no longer trigger
        // since IsLoopEnabled is now false

        Logger.DebugLog(GetType(), "Disabled loop playback");
    }

    private bool hasNextShown;

    /// <summary>
    /// Updates the playback header state, including playback controls and time display.
    /// </summary>
    /// <remarks>This method updates the enablement of playback controls such as play/pause and stop,  as well
    /// as the elapsed and total time display based on the current playback state. It ensures that the displayed times
    /// and Control states are consistent with the playback engine's current position, duration, and state (e.g.,
    /// playing, paused).</remarks>
    private void UpdateHeader()
    {
        //if (!TrackPlayer.HasPlayer()) return;

        // Skip certain operations during track switching to avoid race conditions
        if (_isSwitchingTracks)
        {
            return;
        }

        // Sync LoopPoints from TrackPlayer if needed
        // BUT: Skip sync if we're actively looping with a selection to prevent clearing it
        if (!IsLoopEnabled || !HasSelection)
        {
            var trackLoopPoints = TrackPlayer.LoopPoints ?? [];

            // Only sync if the counts differ OR the contents are actually different
            // Use LINQ SequenceEqual which compares by value, not reference
            bool needsSync = trackLoopPoints.Count != LoopPoints.Count;
            if (!needsSync)
            {
                if (trackLoopPoints.Count > 0)
                {
                    // Only check SequenceEqual if there are items (avoid empty array reference issues)
                    needsSync = !trackLoopPoints.SequenceEqual(LoopPoints);
                }
            }
            else
            {
                // Preserve the current selection index before clearing
                var previousIndex = CurrentLoopPointIndex;

                LoopPoints.Clear();
                foreach (var loopPoint in trackLoopPoints)
                {
                    LoopPoints.Add(loopPoint);
                }

                // Restore selection if it's still valid
                if (previousIndex >= 0 && previousIndex < LoopPoints.Count)
                {
                    CurrentLoopPointIndex = previousIndex;
                }
            }
        }

        if (Duration != TrackPlayer.Duration)
        {
            Duration = TrackPlayer.Duration;
        }



        // Check if we need to constrain playback to loop range
        // IMPORTANT: Only access PlayerRouter if it's not null
        if (IsLoopEnabled && HasSelection && CanStop && TrackPlayer.PlayerRouter != null)
        {
            TrackPlayer.PlayerRouter.PlaybackSpeed = PlaybackSpeed;
            // If current position is outside the loop range, seek to loop start
            if (Position < SelectionStartTime || Position >= SelectionEndTime)
            {
                TrackPlayer.Seek(SelectionStartTime);
                SeekPosition = SelectionStartTime;
                Logger.DebugLog(GetType(),
                    $"Looped: Position {Position:mm\\:ss} outside range, seeking to {SelectionStartTime:mm\\:ss}");
                SeekPosition = TimeSpan.MinValue;
            }
        }

        // Update enablement
        if (CanStop)
        {
            _posElapsedMilliseconds = TrackPlayer.Position.TotalMilliseconds;
            ElapsedTime = TimeSpan.FromMilliseconds(_posElapsedMilliseconds);
            if (TotalTime == TimeSpan.Zero || TotalTime.TotalMilliseconds != TrackPlayer.Duration.TotalMilliseconds)
            {
                _durationMilliseconds = TrackPlayer.Duration.TotalMilliseconds;
                TotalTime = TimeSpan.FromMilliseconds(_durationMilliseconds);
            }
        }
        else
        {
            ElapsedTime = TimeSpan.FromSeconds(0);
            TotalTime = TimeSpan.FromSeconds(0);
        }
        OnPropertyChanged(nameof(ElapsedText));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(IsActivelyPlaying));
        OnPropertyChanged(nameof(CanPlayPause));
        UpdateCommandExecutionStates();

        var totalSecs = (int)ElapsedTime.TotalSeconds;
        var durSecs = (int)Duration.TotalSeconds;

        if (!hasNextShown && durSecs == totalSecs + 10 && AudioTrackQueue.Count > 0)
        {
            var upNext = new InfoBar()
            {
                VerticalContentAlignment = VerticalAlignment.Bottom,
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new Thickness(0, 5, 5, 5),
                Width = 270,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                IsClosable = true,
                IconSource = new DrawingImage(new GeometryDrawing(App.Current?.TryFindResource(AdonisUI.Brushes.ForegroundBrush) as SolidColorBrush, null, MusicIconData)),
                Title = "Up Next",
                CornerRadius = new CornerRadius(6),
                Background = new LinearGradientBrush
                {
                    GradientStops =
                    [
                        new GradientStop(Color.FromArgb(0x99, 0x00, 0x72, 0xDD), 0.0),
                        new GradientStop(Color.FromArgb(0x33, 0x00, 0x72, 0xDD), 1.0)
                    ],
                    Opacity = 0.9,
                    StartPoint = new Point(1, 0),
                    EndPoint = new Point(1, 1)
                },
                Message = $"{AudioTrackQueue[0].Name}",
                Severity = InfoBarSeverity.Informational
            };
            ShowInfoBar(upNext);
            hasNextShown = true;
        }
    }

    /// <summary>
    /// Handles periodic updates for the audio playback position and visualizer components.
    /// </summary>
    /// <remarks>This method is triggered by a timer and updates the current playback position, processes
    /// audio data for the level meter visualizer, and calculates the RMS and peak values for the audio signal. It also
    /// ensures that the header information is updated accordingly. The method operates only if both the <see
    /// cref="LevelMeterVisualizer"/> and <see cref="AudioAnalyzer"/> are initialized.</remarks>
    /// <param name="sender">The source of the timer event. This parameter is not used in the method.</param>
    /// <param name="e">The event data associated with the timer tick. This parameter is not used in the method.</param>
    private void PositionTimerTick(object? sender, EventArgs e)
    {
        Position = TrackPlayer.Position;
        OnPropertyChanged(nameof(Position));
        if (!IsSeeking)
        {
            SeekPosition = Position;
        }
        UpdateHeader();
    }

    /// <summary>
    /// Handles errors that occur during track playback.
    /// </summary>
    /// <remarks>This method logs the error details for debugging purposes. Ensure that the provided exception
    /// contains meaningful information about the error to facilitate troubleshooting.</remarks>
    /// <param name="ex">The exception representing the error that occurred. Cannot be <see langword="null"/>.</param>
    private void OnTrackPlayerError(Exception ex)
    {
        Logger.DebugLog(GetType(), $"TrackPlayer: {ex.Message}");
    }

    public new void Dispose()
    {
        _trackPlayerSubscription?.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// Stops all currently playing audio, with behavior determined by the state of the Control key.
    /// </summary>
    /// <remarks>If the Control key is pressed, all audio tracks are stopped. Otherwise, only the current
    /// audio track is stopped.</remarks>
    /// <param name="param">An optional parameter that is not used by this method.</param>
    [RelayCommand(CanExecute = nameof(CanExecuteStop))]
    public async Task StopAudio(object? param)
    {
        IsLoopingEnabled = false;
        if (Keyboard.Modifiers == ModifierKeys.Control || (param is bool All && All))
        {
            Logger.DebugLog(GetType(), $"Stopping All Audio");
            AudioEngine.StopAllAudio();
        }
        else
        {
            Logger.DebugLog(GetType(), $"Stopping Audio Track");
            TrackPlayer.Stop();
        }
    }

    [RelayCommand]
    private void CopyAudioUri(Audio? audio)
    {
        if (audio != null && audio.CopyUriToClipboard())
        {
            MainWindow.ShowNotification("URI copied to clipboard", $"streamflow://play/{audio.Id}");
        }
        else
        {
            MainWindow.ShowNotification("Failed to copy URI", "Please try again");
        }
    }

    private bool CanIncreaseVolume()
    {
        return CurrentVolume < 1 && CanStop;
    }
    private bool CanDecreaseVolume()
    {
        return CurrentVolume > 0 && CanStop;
    }
    private bool CanMuteVolume()
    {
        return CanStop && (CurrentVolume == 0 || VolumeState == 0);
    }

    private bool CanExecuteStop()
    {
        return CanStop;
    }

    [ObservableProperty]
    private bool _trackLoaded;

    private bool CanExecutePlayPause()
    {
        return CanStop;
    }

    [RelayCommand]
    private async Task PlayAudioItem(object? param)
    {
        if (AudioTrackQueue.Count > 0)
        {
            AudioTrackQueue.Clear();
        }
        await PlayAudio(param);
    }

    /// <summary>When on, PlayNextTrack picks a random track from the library instead of the
    /// next one in list order. Purely a player-transport convenience — doesn't touch
    /// AudioTrackQueue (the explicit "queue this next" feature), which stays FIFO regardless.</summary>
    [ObservableProperty]
    private bool _isShuffleEnabled;

    /// <summary>When on, the current track restarts instead of stopping once it reaches the
    /// end — see the PlaybackEnded handling in OnTrackPlayerEvent.</summary>
    [ObservableProperty]
    private bool _isRepeatEnabled;

    /// <summary>All loaded tracks in the library's current display order — the pool
    /// PlayNextTrack/PlayPreviousTrack cycle through. Excludes SoundEffects (this is a "next
    /// track" transport control, not a "next item of any kind").</summary>
    private List<AudioTrack> LibraryTracks => AudioListCollectionView?.Cast<object>().OfType<AudioTrack>().ToList() ?? [];

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task PlayNextTrack()
    {
        var tracks = LibraryTracks;
        if (tracks.Count == 0) return;

        AudioTrack next;
        if (IsShuffleEnabled && tracks.Count > 1)
        {
            var rng = Random.Shared;
            do { next = tracks[rng.Next(tracks.Count)]; } while (ReferenceEquals(next, AudioTrack));
        }
        else
        {
            var index = tracks.FindIndex(t => ReferenceEquals(t, AudioTrack));
            next = tracks[(index + 1) % tracks.Count];
        }

        await PlayAudioItem(next);
    }

    /// <summary>Standard media-player convention: restarts the current track if more than a
    /// few seconds in, otherwise jumps to the previous track in the library — so a quick
    /// double-tap of Previous actually goes back a track instead of just re-triggering the
    /// current one's intro over and over.</summary>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task PlayPreviousTrack()
    {
        if (ElapsedTime.TotalSeconds > 3)
        {
            SeekPosition = TimeSpan.Zero;
            return;
        }

        var tracks = LibraryTracks;
        if (tracks.Count == 0) return;

        var index = tracks.FindIndex(t => ReferenceEquals(t, AudioTrack));
        var previous = tracks[(index - 1 + tracks.Count) % tracks.Count];
        await PlayAudioItem(previous);
    }

    [RelayCommand]
    private async Task QueueAudioItem(object? param)
    {
        if (param is AudioTrack audio)
        {
            await QueueAudioTrack(audio);
        }
    }

    [RelayCommand]
    private async Task QueueAudioItemNext(object? param)
    {
        if (param is AudioTrack audio)
        {
            await QueueAudioTrack(audio, true);
        }
    }

    public async Task QueueAudioTrack(AudioTrack audioItem, bool next = false)
    {
        if (next)
        {
            AudioTrackQueue.Insert(0, audioItem);
        }
        else
        {
            AudioTrackQueue.Add(audioItem);
        }
        if (!IsPlaying)
        {
            var nextAudio = AudioTrackQueue[0];
            AudioTrackQueue.Remove(nextAudio);
            await PlayAudio(nextAudio);
        }
    }

    /// <summary>
    /// Plays the specified audio track or toggles playback state based on the provided parameter.
    /// </summary>
    /// <remarks>- If the parameter is an <see cref="AudioTrack"/> and matches the currently loaded track, the
    /// playback state is toggled. - If the parameter is an <see cref="AudioTrack"/> that differs from the currently
    /// loaded track, the new track is loaded and played. - If the parameter is a <see cref="SoundEffect"/>, the sound
    /// effect is played. - If the parameter is an <see cref="AudioViewModel"/>, the playback state is toggled.  The
    /// method ensures that necessary audio analyzers and visualizers are initialized when playing a new
    /// track.</remarks>
    /// <param name="param">The audio object to play, which can be an <see cref="AudioTrack"/>, <see cref="SoundEffect"/>, or an <see
    /// cref="AudioViewModel"/>. If <paramref name="param"/> is <see langword="null"/>, the method does nothing.</param>
    /// <returns></returns>
    [RelayCommand(CanExecute = nameof(CanExecutePlayPause))]
    private async Task PlayAudio(object? param)
    {
        if (param is not null && (param is Audio RequestedAudio))
        {
            if (InfoBarTimer is null && MainWindow.Current?.ibPresenter is not null)
            {
                InfoPanel = MainWindow.Current.ibPresenter;
                InfoBarTimer = new(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, InfoBarTimerTick, UIDispatcher);
                InfoBarTimer.Start();
            }

            PositionTimer ??= new(TimeSpan.FromMilliseconds(50), DispatcherPriority.Render, PositionTimerTick, UIDispatcher);

            // Module files (tracker formats) are handled directly by TrackPlayer
            // They use the same playback flow as other audio files
            var isTrackerFormat = FileExtension.EndsWith(AppModel.Instance.ValidModuleExtensions, RequestedAudio.FilePath.ToLower());

            if (isTrackerFormat)
            {
                Logger.InfoLog(GetType(), $"Detected tracker format: {RequestedAudio.FilePath}");

                // Test module first to verify it's valid
                if (!Xmp.TestModule(RequestedAudio.FilePath, out var testInfo))
                {
                    Logger.ErrorLog(GetType(), $"Invalid module file: {RequestedAudio.FilePath}");
                    await ShowXmpTestResult(
                        "Invalid Module File",
                        $"The file '{Path.GetFileName(RequestedAudio.FilePath)}' is not a valid tracker module.",
                        InfoBarSeverity.Error);
                    return;
                }

                Logger.InfoLog(GetType(),
                    $"Module validated: {testInfo.Name} ({testInfo.Format})");

                // Module playback flows through the normal audio system
                // TrackPlayer.LoadAudio() will detect it's a module and handle it appropriately
                if (RequestedAudio is AudioTrack audioToPlay)
                {
                    // Show the "now playing" info for modules too
                    IsLoadingTrack = true;
                    LoadingMessage = $"Loading module: {audioToPlay.Name}...";

                    try
                    {
                        // Use the existing audio playback flow - it now handles modules!
                        goto StandardAudioPlayback;
                    }
                    finally
                    {
                        IsLoadingTrack = false;
                    }
                }
            }

            StandardAudioPlayback:
            {
                if (RequestedAudio is AudioTrack audioToPlay)
                {

                    try
                    {
                        // If a track is currently playing, stop it first
                        if (IsPlaying)
                        {
                            // Set flag to indicate we're switching tracks
                            _isSwitchingTracks = true;
                            Logger.InfoLog(GetType(), $"Stopping current track before switching: '{AudioTrack?.Name}'");
                            TrackPlayer.Stop();

                            // Remove the player component from mixer
                            if (AudioEngine.PlayerInitialized())
                            {
                                AudioEngine.RemovePlayer();
                            }

                            // Give a small delay to ensure stop completes
                            await Task.Delay(50);

                            // Manually invoke stop UI updates since we're ignoring the event
                            await UIDispatcher.InvokeAsync(() =>
                            {
                                TrackLoaded = false;
                            });
                        }

                        AudioFilePath = audioToPlay.FilePath;

                        Logger.InfoLog(GetType(), $"Loading audio track: {audioToPlay.Name}");

                        if (!await AudioEngine.PlayAudio(audioToPlay, progress =>
                        {
                            OnPropertyChanged(nameof(IsPlaying));
                            // Update progress on UI thread
                            UIDispatcher!.InvokeAsync(() =>
                            {
                                LoadingMessage = $"Rendering audio: {audioToPlay.Name}... {progress}%";
                            });
                        }))
                        {
                            await UIDispatcher!.InvokeAsync(() => IsLoadingTrack = false);
                            StopPlayback(new TrackPlayerEventArgs(Statuses.Error, PlaybackState, []));
                            Logger.ErrorLog(GetType(), $"Failed to load audio track: '{audioToPlay.Name}'");
                            return;
                        }

                        // Hide loading indicator
                        await UIDispatcher!.InvokeAsync(() => IsLoadingTrack = false);

                        // Audio is now loaded and playing, load visualization asynchronously
                        Logger.InfoLog(GetType(), $"Audio loaded, now loading visualization for: {audioToPlay.Name}");

                        // Update UI with visualization (audio already playing from above)
                        TrackLoaded = true;
                        CurrentVolume = TrackPlayer.PlayerRouter!.Volume;
                        OnPropertyChanged(nameof(TrackLoaded));
                        StartPlayback(new TrackPlayerEventArgs(Statuses.Loaded, PlaybackState, []));
                        ProcessNowPlaying(audioToPlay);
                        Logger.InfoLog(GetType(), $"Track playback ready: '{audioToPlay.Name}'");

                    }
                    finally
                    {
                        // Always clear the switching flag
                        _isSwitchingTracks = false;
                    }
                }
                else if (AudioTrack?.Name == RequestedAudio.Name)
                {
                    await AudioEngine.PlayPauseAudio(PlaybackState);
                }
                else if (RequestedAudio is SoundEffect soundToPlay)
                {
                    await AudioEngine.PlayAudio(soundToPlay);
                }
            } // End StandardAudioPlayback block
        }
        else
        {
            await AudioEngine.PlayPauseAudio(PlaybackState);
        }
    }


    /// <summary>
    /// Displays the specified <see cref="InfoBar"/> in the user interface.
    /// </summary>
    /// <remarks>The method ensures that the operation is executed on the UI thread. If the dispatcher has
    /// already started shutting down, the operation will not be performed.</remarks>
    /// <param name="infobar">The <see cref="InfoBar"/> instance to display. This parameter cannot be null.</param>
    private void ShowInfoBar(InfoBar infobar)
    {
        if (UIDispatcher is not null && !UIDispatcher.HasShutdownStarted && InfoPanel is not null)
        {
            UIDispatcher.BeginInvoke(() =>
            {
                infobar.IsOpen = true;
                InfoPanel.Children.Add(infobar);
            });
        }
    }

    /// <summary>
    /// Displays the specified <see cref="InfoBar"/> in the user interface.
    /// </summary>
    /// <remarks>The method ensures that the operation is executed on the UI thread. If the dispatcher has
    /// already started shutting down, the operation will not be performed.</remarks>
    /// <param name="infobar">The <see cref="InfoBar"/> instance to display. This parameter cannot be null.</param>
    private void ShowUpNext(InfoBar infobar)
    {
        if (UIDispatcher is not null && !UIDispatcher.HasShutdownStarted && InfoPanel is not null)
        {
            UIDispatcher.BeginInvoke(() =>
            {
                infobar.IsOpen = true;
                InfoPanel.Children.Add(infobar);
            });
        }
    }

    /// <summary>
    /// Displays an informational message about the currently playing audio track.
    /// </summary>
    /// <remarks>This method creates an informational message bar with details about the specified audio track
    /// and adds it to the collection of active information bars. The message bar is configured to automatically close
    /// after 5 seconds.</remarks>
    /// <param name="audioToPlay">The <see cref="AudioTrack"/> object representing the audio track that is currently playing. Cannot be null.</param>
    private void ProcessNowPlaying(AudioTrack audioToPlay)
    {
        var nowPlaying = new InfoBar()
        {
            VerticalContentAlignment = VerticalAlignment.Bottom,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 5, 5, 5),
            Width = 270,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            IconSource = new DrawingImage(new GeometryDrawing(App.Current?.TryFindResource(AdonisUI.Brushes.ForegroundBrush) as SolidColorBrush, null, MusicIconData)),
            IsClosable = true,
            Title = "Now Playing",
            CornerRadius = new CornerRadius(6),
            Background = new LinearGradientBrush
            {
                GradientStops =
                [
                    new GradientStop(Color.FromArgb(0x88, 0x00, 0x72, 0xEE), 0.0),
                    new GradientStop(Color.FromArgb(0x22, 0x00, 0x72, 0xEE), 1.0)
                ],
                Opacity = 0.9,
                StartPoint = new Point(1, 0),
                EndPoint = new Point(1, 1)
            },
            Message = $"{audioToPlay.Name}",
            Severity = InfoBarSeverity.Informational
        };
        activeInfoBars.Add(DateTime.Now.AddSeconds(5), nowPlaying);
    }

    /// <summary>
    /// Hides the specified <see cref="InfoBar"/> and removes it from the user interface.
    /// </summary>
    /// <remarks>This method ensures that the operation is performed on the UI thread. If the dispatcher has
    /// already started shutting down, the method exits without performing any action.</remarks>
    /// <param name="infobar">The <see cref="InfoBar"/> instance to hide and remove. Must not be null.</param>
    [RelayCommand]
    private void HideNowPlaying(InfoBar infobar)
    {
        if (UIDispatcher is not null && UIDispatcher.HasShutdownStarted)
        {
            return;
        }
        else
        {
            UIDispatcher?.BeginInvoke(() =>
            {
                infobar.IsOpen = false;
                InfoPanel?.Children.Remove(infobar);
            });
        }
    }

    private double VolumeState;

    [RelayCommand(CanExecute = nameof(CanIncreaseVolume))]
    private void VolumeIncrease(string param)
    {
        if (CurrentVolume >= 0)
        {
            if (CurrentVolume < 0.91)
            {
                CurrentVolume += 0.1;
            }
            else
            {
                CurrentVolume = 1;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanDecreaseVolume))]
    private void VolumeDecrease(string param)
    {
        if (CurrentVolume <= 1)
        {
            if (CurrentVolume > 0.1)
            {
                CurrentVolume -= 0.1;
            }
            else
            {
                CurrentVolume = 0;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanMuteVolume))]
    private void VolumeMute(string param)
    {
        if (VolumeState == 0 && CurrentVolume != 0)
        {
            VolumeState = CurrentVolume;
            CurrentVolume = 0;
        }
        else
        {
            CurrentVolume = VolumeState;
            VolumeState = 0;
        }
    }

    private async void UpdateCommandExecutionStates()
    {
        if (UIDispatcher != null)
        {
            await UIDispatcher.InvokeAsync(() =>
            {
                StopAudioCommand.NotifyCanExecuteChanged();
                PlayAudioCommand.NotifyCanExecuteChanged();
                TogglePlaybackSpeedCommand.NotifyCanExecuteChanged();
                VolumeDecreaseCommand.NotifyCanExecuteChanged();
                VolumeIncreaseCommand.NotifyCanExecuteChanged();
                VolumeMuteCommand.NotifyCanExecuteChanged();
            });
        }
    }

    /// <summary>
    /// Retrieves the first <see cref="Button"/> ancestor of the specified <see cref="DependencyObject"/> in the visual
    /// tree.
    /// </summary>
    /// <param name="source">The starting <see cref="DependencyObject"/> from which to search for a <see cref="Button"/> ancestor.</param>
    /// <returns>The first <see cref="Button"/> found in the visual tree hierarchy, or <see langword="null"/> if no <see
    /// cref="Button"/> is found.</returns>
    private static Button? GetAudioItemButton(DependencyObject source)
    {
        var current = source;

        while (current != null)
        {
            if (current is Button button)
            {
                return button;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    /// <summary>
    /// Clears all currently displayed "Now Playing" information from the UI.
    /// </summary>
    /// <remarks>This method closes all open information bars and removes them from the panel.  It ensures
    /// that the operation is performed on the UI thread with a background priority.</remarks>
    private void ClearNowPlaying()
    {
        UIDispatcher?.BeginInvoke(() =>
        {
            if (InfoPanel?.Children.Count > 0)
            {
                var children = new UIElement[InfoPanel.Children.Count];
                InfoPanel.Children.CopyTo(children, 0);
                foreach (var child in children)
                {
                    if (child is InfoBar item)
                    {
                        item.IsOpen = false;
                    }
                }
                InfoPanel.Children.Clear();
            }
        });
    }

    /// <summary>
    /// Test method to increment the counter property. Present only for testing purposes.
    /// </summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void IncrementCounter()
    {
        Count++;
    }

    /// <summary>
    /// Toggles the playback speed of the audio player between normal speed and an increased speed.
    /// </summary>
    /// <remarks>This method adjusts the playback speed of the audio player. If the current playback speed is
    /// at or below 1.4x,  it increases the speed incrementally. If the speed exceeds this threshold, it resets to
    /// normal speed (1.0x). The method must be called on the UI thread as it uses the dispatcher to invoke the
    /// operation.</remarks>
    /// <param name="param">An optional parameter that is currently unused. Can be <see langword="null"/>.</param>
    [RelayCommand(CanExecute = nameof(CanTogglePlaybackSpeed))]
    private void TogglePlaybackSpeed(object? param)
    {
        _ = param;
        Dispatcher.CurrentDispatcher.Invoke(() =>
        {
            //if (Engine is not null && Engine.RequestedAudio.Player is not null)
            //{
            //    if (Engine.RequestedAudio.Player.PlaybackSpeed <= 1.4f)
            //        Engine.RequestedAudio.Player.PlaybackSpeed += 0.1f;
            //    else
            //        Engine.RequestedAudio.Player.PlaybackSpeed = 1f;
            //}
        });
    }

    private bool CanTogglePlaybackSpeed()
    {
        return CanStop;
    }

    private Statuses PreviousStatus;
    private PlaybackState PreviousState;

    /// <summary>
    /// Handles changes in the track player's playback state and performs the appropriate action based on the new state.
    /// </summary>
    /// <remarks>This method responds to changes in the playback state of the track player by invoking the
    /// corresponding action for the new state. For example, it pauses playback when the state is <see
    /// cref="PlaybackState.Paused"/>, stops playback when the state is <see cref="PlaybackState.Stopped"/>, and resumes
    /// playback when the state is <see cref="PlaybackState.Playing"/>. No action is taken for unrecognized
    /// states.</remarks>
    /// <param name="args">The event arguments containing the updated playback state and status of the track player.</param>
    private void OnTrackPlayerChanged(TrackPlayerEventArgs args)
    {
        // Ignore state updates when switching tracks to prevent race conditions
        if (_isSwitchingTracks && args.PlaybackState == PlaybackState.Stopped)
        {
            Logger.DebugLog(GetType(), $"Ignoring stop event during track switch");
            return;
        }

        PlaybackState = args.PlaybackState;
        Status = args.Status;
        AudioTrack = TrackPlayer.AudioTrack;

        if (args.Status != PreviousStatus || args.PlaybackState != PreviousState)
        {
            PreviousState = args.PlaybackState;
            PreviousStatus = args.Status;
            var LogMessage = $"TrackPlayer event => Status: {Status} - Playback State: {PlaybackState}";
            Logger.DebugLog(GetType(), LogMessage);
        }

        if (!IsLoopingEnabled)
        {

            // Handle state transitions based on actual state changes
            switch (PlaybackState)
            {
                case PlaybackState.Playing:
                    // Only start playback if we weren't already playing
                    StartPlayback(args);
                    break;

                case PlaybackState.Paused:
                    // Only pause if we transition from playing to paused
                    PausePlayback(args);
                    break;

                case PlaybackState.Stopped:
                    // Always stop when stopped state is received (unless switching tracks)
                    if (!_isSwitchingTracks && (Status == Statuses.Reset || Status == Statuses.PlaybackEnded))
                    {
                        StopPlayback(args);
                    }
                    break;

                default:
                    break;
            }
        }

        if (Status == Statuses.PlaybackEnded)
        {
            if (AudioTrackQueue.Count > 0)
            {
                var nextAudio = AudioTrackQueue[0];
                AudioTrackQueue.Remove(nextAudio);
                hasNextShown = false;
                PlayAudio(nextAudio);
            }
            else if (IsRepeatEnabled && AudioTrack != NullAudio.NullTrack)
            {
                _ = PlayAudioItem(AudioTrack);
            }
        }
    }

    /// <summary>
    /// Resumes playback of the current track and updates the playback state.
    /// </summary>
    /// <remarks>This method sets the playback state to <see cref="PlaybackState.Playing"/> and starts the
    /// position timer. If <paramref name="args"/> is not null, the method updates relevant values using the provided
    /// data.</remarks>
    /// <param name="args">Optional event arguments containing track-specific data to update. If provided, the values are populated
    /// accordingly.</param>
    private void StartPlayback(TrackPlayerEventArgs? args = null)
    {
        PositionTimer?.Start();
        UpdateHeader();
        Logger.DebugLog(GetType(), $"UI: Playback started - Status: {Status}, State: {PlaybackState}");
    }

    /// <summary>
    /// Pauses the current playback and updates the playback state.
    /// </summary>
    /// <remarks>This method stops the playback timer, updates the playback state to paused, and adjusts the
    /// playback position or other values based on the provided event data. If <paramref name="args"/> is null, the
    /// playback position is reset to the seek position.</remarks>
    /// <param name="args">Optional. Contains event data used to update playback values. If null, the playback position is reset to the
    /// seek position.</param>
    private void PausePlayback(TrackPlayerEventArgs? args = null)
    {
        PositionTimer?.Stop();
        UpdateHeader();
        Logger.DebugLog(GetType(), $"UI: Playback paused - Status: {Status}, State: {PlaybackState}");
    }

    /// <summary>
    /// Stops the current playback and resets the playback state.
    /// </summary>
    /// <remarks>This method halts the playback timer, updates the playback state to indicate that playback
    /// has stopped,  and resets related properties such as the playback position and RMS value. It also clears the "Now
    /// Playing" information.</remarks>
    /// <param name="args">The event arguments containing information about the track playback to be stopped.</param>
    private void StopPlayback(TrackPlayerEventArgs args)
    {
        PositionTimer?.Stop();
        TrackLoaded = false;
        CurrentVolume = 0;
        ClearSelection();
        ClearNowPlaying();
        UpdateHeader();
    }


    /// <summary>
    /// Determines whether the specified audio is currently playing.
    /// </summary>
    /// <param name="audio">The audio instance to check. This can be an <see cref="AudioTrack"/> or a <see cref="SoundEffect"/>.</param>
    /// <returns><see langword="true"/> if the specified audio is currently playing; otherwise, <see langword="false"/>.</returns>
    public bool IsAudioPlaying(Audio audio)
    {
        return audio switch
        {
            AudioTrack track => AudioTrack?.Name == track.Name,
            SoundEffect effect => PlayingSoundEffects.Contains(effect.Name),
            _ => false
        };
    }

    /// <summary>
    /// Determines whether the specified audio object matches the current filter criteria.
    /// </summary>
    /// <remarks>The filter criteria are based on the application's current filter options, including: <list
    /// Type="bullet"> <item><description>Search term matching the audio name or category
    /// (case-insensitive).</description></item> <item><description>Selected tags associated with the audio
    /// object.</description></item> <item><description>Audio Type inclusion (e.g., audio tracks or sound
    /// effects).</description></item> </list> If the object is not of Type <see cref="Audio"/>, the method returns <see
    /// langword="false"/>.</remarks>
    /// <param name="obj">The object to evaluate. Must be of Type <see cref="Audio"/>.</param>
    /// <returns><see langword="true"/> if the audio object matches the filter criteria; otherwise, <see langword="false"/>.</returns>
    private bool FilterAudio(object obj)
    {
        if (obj is Audio audio)
        {
            var containsTags = false;
            var containsCategory = audio.Category.Name.Contains(AppModel.Instance.Settings.FilterOptions.SearchTerm, StringComparison.OrdinalIgnoreCase);
            var containsString = audio.Name.Contains(AppModel.Instance.Settings.FilterOptions.SearchTerm, StringComparison.OrdinalIgnoreCase);
            var typeIncluded = false;

            if (AppModel.Instance.Settings.FilterOptions.IncludeAudioTracks && AppModel.Instance.Settings.FilterOptions.IncludeSoundEffects)
            {
                typeIncluded = true;
            }
            else if (AppModel.Instance.Settings.FilterOptions.IncludeAudioTracks && audio is SoundEffect)
            {
                typeIncluded = false;
            }
            else if (AppModel.Instance.Settings.FilterOptions.IncludeSoundEffects && audio is AudioTrack)
            {
                typeIncluded = false;
            }
            else
            {
                typeIncluded = true;
            }

            if (AppModel.Instance.Settings.FilterOptions.SelectedTags.Where(x => x.Selected).Any())
            {
                foreach (AudioTag tag in AppModel.Instance.Settings.FilterOptions.SelectedTags.Where(x => x.Selected))
                {
                    if (audio.Tags.Contains(tag))
                    {
                        containsTags = true;
                    }
                }
            }
            else
            {
                containsTags = true;
            }
            return typeIncluded && containsTags && (containsCategory || containsString);
        }
        return false;
    }

    [RelayCommand]
    private static async Task EditAudioProperties(object? param)
    {
        if (param is null)
            return;
        if (param is not null && param is Audio audio)
        {
            var dlg = App.Services.GetService(typeof(IDialogService)) as IDialogService;
            if (dlg is not null)
            {
                await dlg.PropertiesDialog(audio);
            }
            //var PropFlyout = new Flyout()
            //{
            //    Content = new PropertiesEditor() { Audio = menuItem.Tag as Audio },
            //    Placement = FlyoutPlacementMode.RightEdgeAlignedTop,
            //    ShowMode = FlyoutShowMode.Standard,
            //};
            //PropFlyout.ShowAt(menuItem);
        }
    }

    /// <summary>
    /// Applies the specified filter and sort options to the audio collection view.
    /// </summary>
    /// <remarks>This method updates the grouping and sorting of the audio collection view based on the
    /// provided <paramref name="filterOptions"/>. If both sound effects and audio tracks are  excluded, no items will
    /// be displayed. The method also supports grouping by category  when the sort Type is set to <see
    /// cref="SortType.CATEGORY"/>.</remarks>
    /// <param name="filterOptions">The filter options to apply, including whether to include sound effects, audio tracks,  and the desired sort
    /// Type and direction.</param>
    //public void ApplyFilterOptions(FilterOptions filterOptions)
    //{
    //    if (AudioListCollectionView is null)
    //        return;
    //    AudioListCollectionView.GroupDescriptions.Delete();
    //    AudioListCollectionView.SortDescriptions.Delete();

    //    switch (filterOptions.SortType)
    //    {
    //        case SortType.NAME:
    //            AudioListCollectionView.SortDescriptions.Add(new SortDescription("Name", filterOptions.SortDirection));
    //            break;
    //        case SortType.CATEGORY:
    //            AudioListCollectionView.GroupDescriptions.Add(new PropertyGroupDescription("Category.Name"));
    //            AudioListCollectionView.SortDescriptions.Add(new SortDescription("Category.Name", filterOptions.SortDirection));
    //            AudioListCollectionView.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
    //            break;
    //        default:
    //            break;
    //    }
    //    AudioListCollectionView.Refresh();
    //}
}
