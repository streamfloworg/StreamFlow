using System.ComponentModel;
using System.Windows.Shapes;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xaml.Behaviors;

using RichCanvas.Helpers;

using StreamFlow.App.Helpers;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.App.Views.Windows;
using StreamFlow.Core.AudioHandling;
using StreamFlow.Core.AudioProperties;
using StreamFlow.Core.Cache;
using StreamFlow.Core.Data;
using StreamFlow.Core.Helpers;

using UserControl = System.Windows.Controls.UserControl;

namespace StreamFlow.App.Controls;

[ObservableObject]
public sealed partial class AudioControls : UserControl
{
    public static Visibility IsDebug
    {
    #if DEBUG
        get { return Visibility.Visible; }
    #else
        get { return Visibility.Collapsed; }
    #endif
    }

    public static AudioEngine Engine => AudioEngine.Instance;

    public AudioViewModel ViewModel { get; }

    public bool Resuming { get; private set; }
    public bool RepeatEnabled { get; set; }
    [ObservableProperty]
    private TaskbarItemInfo? taskbarItem;

    private int _barCount = 56;
    private double[] _currentHeights = new double[56];
    private float[] _rawAnalyzerValues = new float[56];

    public AudioControls()
    {
        ViewModel = App.Services.GetRequiredService<AudioViewModel>();
        DataContext = ViewModel;
        InitializeComponent();

        Loaded += AudioControls_Loaded;
        Unloaded += AudioControls_Unloaded;
    }

    private void AudioControls_Loaded(object sender, RoutedEventArgs e)
    {
        InitializeWaveformRectangles();
        WaveformDisplay.SizeChanged += WaveformDisplay_SizeChanged;
        CompositionTarget.Rendering += OnRendering;
    }

    private void AudioControls_Unloaded(object sender, RoutedEventArgs e)
    {
        WaveformDisplay.SizeChanged -= WaveformDisplay_SizeChanged;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void WaveformDisplay_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double width = WaveformDisplay.ActualWidth;
        if (width <= 0) return;

        // Space needed per bar: 3px width + 2px margin = 5px total
        int newBarCount = (int)Math.Max(10, Math.Floor(width / 5.0));
        if (newBarCount != _barCount)
        {
            InitializeWaveformRectangles();
        }
    }

    private void InitializeWaveformRectangles()
    {
        double width = WaveformDisplay.ActualWidth;
        int targetBarCount = 56;
        if (width > 0)
        {
            targetBarCount = (int)Math.Max(10, Math.Floor(width / 5.0));
        }

        _barCount = targetBarCount;
        _currentHeights = new double[_barCount];
        _rawAnalyzerValues = new float[_barCount];

        WaveformDisplay.Children.Clear();

        for (var i = 0; i < _barCount; i++)
        {
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = 3,
                Margin = new Thickness(0, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Height = 4.0
            };
            rect.SetResourceReference(Shape.FillProperty, "AccentDimBrush");
            WaveformDisplay.Children.Add(rect);
            _currentHeights[i] = 4.0;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        bool isPlaying = ViewModel.IsActivelyPlaying;
        int currentBarCount = _barCount;

        if (isPlaying && AudioEngine.WaveformAnalyzer != null)
        {
            AudioEngine.WaveformAnalyzer.GetWaveformHeights(_rawAnalyzerValues);

            for (int i = 0; i < currentBarCount; i++)
            {
                // Apply a sinus-based envelope to focus peak visual activity towards the middle (matching original styling)
                double t = currentBarCount > 1 ? i / (double)(currentBarCount - 1) : 0.5;
                double envelope = 0.35 + 0.65 * Math.Sin(t * Math.PI);

                // Map raw float value (0..1) to target visual height (4..28)
                double targetHeight = 4.0 + (_rawAnalyzerValues[i] * envelope * 24.0);

                // Apply attack & decay smoothing (instant rise, organic exponential decay)
                if (targetHeight > _currentHeights[i])
                {
                    _currentHeights[i] = targetHeight; // Instant rise / fast attack
                }
                else
                {
                    _currentHeights[i] = _currentHeights[i] + (targetHeight - _currentHeights[i]) * 0.15; // Smooth exponential decay
                }
            }
        }
        else
        {
            // Decaying smoothly back to the baseline of 4.0
            for (int i = 0; i < currentBarCount; i++)
            {
                _currentHeights[i] = _currentHeights[i] + (4.0 - _currentHeights[i]) * 0.12;
            }
        }

        // Apply heights directly to shapes
        for (int i = 0; i < currentBarCount; i++)
        {
            if (i < WaveformDisplay.Children.Count && WaveformDisplay.Children[i] is System.Windows.Shapes.Rectangle rect)
            {
                rect.Height = _currentHeights[i];
            }
        }
    }

    private void ViewClosing(object sender, CancelEventArgs e)
    {
        Dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        AppModel.Instance.SaveData();
        CacheManager.CleanUpCache();
    }

    /// <summary>
    /// Marks the start of a seek drag/click so the ViewModel routes SeekPosition changes to the player
    /// instead of treating them as programmatic position updates.
    /// </summary>
    private void PlaybackProgressSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ViewModel.IsSeeking = true;
    }

    private void PlaybackProgressSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ViewModel.IsSeeking = false;
    }

    private void Slider_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ViewModel.PlaybackSpeed = 1;
    }

    private void Slider_MouseDoubleClick_1(object sender, MouseButtonEventArgs e)
    {
        ViewModel.Panning = 0.5f;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        //SettingsMenu.IsOpen = true;
    }
}
