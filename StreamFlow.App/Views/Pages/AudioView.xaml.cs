using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

// iNKORE usings removed

using StreamFlow.App.Commands;
using StreamFlow.App.Controls;
using StreamFlow.App.Helpers;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.App.Views.Windows;
using StreamFlow.Core.AudioHandling;
using StreamFlow.Core.Data;

using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
// ListView alias removed
using TabControl = System.Windows.Controls.TabControl;

namespace StreamFlow.App.Views.Pages;

[ObservableObject]
public partial class AudioView
{
    public static System.Windows.Controls.ListView? ListView { get; private set; }
    public AudioViewModel ViewModel { get; }

    public int Counter { get; set; }

    [ObservableProperty]
    private Style? _defaultStyle;

    public AudioView(AudioViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        IsPropertiesPinned = false;
        IsFlyoutOpen = false;
        UpdatePanelWidth();
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private readonly Geometry ListViewIconData = App.Current.FindResource("ListViewIconData") as Geometry;
    private readonly Geometry GridViewIconData = App.Current.FindResource("GridViewIconData") as Geometry;

    private Geometry currentViewIconData;

    public Geometry CurrentViewIconData
    {
        get
        {
            if (currentViewIconData is null)
            {
                return GridViewIconData;
            }
            else
            {
                return currentViewIconData;
            }
        }

        private set
        {
            currentViewIconData = value;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if(e.PropertyName == "ViewType")
        {
            switch (ViewModel.ViewType)
            {
                case AudioViewType.GridView:
                    CurrentViewIconData = GridViewIconData;
                    break;
                case AudioViewType.ListView:
                    CurrentViewIconData = ListViewIconData;
                    break;
            }
        }
    }

    private void ViewDragOver(object sender, DragEventArgs e)
    {
        var allowDrop = false;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);

        if (files == null)
        {
            return;
        }

        foreach (var file in files)
        {
            if (FileExtension.EndsWith(AppModel.Instance.ValidAudioExtensions, file) || 
                FileExtension.EndsWith(AppModel.Instance.ValidModuleExtensions, file) ||
                FileExtension.EndsWith(AppModel.Instance.ValidScenePackageExtensions, file))
            {
                allowDrop = true;
                break;
            }
        }

        if (!allowDrop)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    private async void ViewPreviewDrop(object sender, DragEventArgs e)
    {
        List<string> files = [.. (string[])e.Data.GetData(DataFormats.FileDrop)];
        MainWindow.Current?.Activate();
        foreach (var file in files)
        {
            await new FileDragDrop(file).ShowAsync();
        }
    }

    private bool _isFlyoutOpen;
    public bool IsFlyoutOpen
    {
        get => _isFlyoutOpen;
        set
        {
            if (_isFlyoutOpen != value)
            {
                // Do not allow flyout when panel is pinned
                _isFlyoutOpen = value && !_isPropertiesPinned;
                OnPropertyChanged();
            }
        }
    }

    private bool _isPropertiesPinned;
    public bool IsPropertiesPinned
    {
        get => _isPropertiesPinned;
        set
        {
            if (_isPropertiesPinned != value)
            {
                _isPropertiesPinned = value;
                OnPropertyChanged();
                UpdatePanelWidth();
                // If pinning, ensure flyout is closed. If unpinning, do not alter selection.
                if (_isPropertiesPinned && _isFlyoutOpen)
                {
                    _isFlyoutOpen = false;
                    OnPropertyChanged(nameof(IsFlyoutOpen));
                }
            }
        }
    }

    private double _propertiesPanelWidth = 360;
    public double PropertiesPanelWidth
    {
        get => _propertiesPanelWidth;
        set
        {
            if (Math.Abs(_propertiesPanelWidth - value) > double.Epsilon)
            {
                _propertiesPanelWidth = value;
                OnPropertyChanged();
                UpdatePanelWidth();
            }
        }
    }

    private double _panelWidth;
    public double PanelWidth
    {
        get => _panelWidth;
        private set
        {
            if (Math.Abs(_panelWidth - value) > double.Epsilon)
            {
                _panelWidth = value;
                OnPropertyChanged();
            }
        }
    }

    public record ModifierOption(string Name, ModifierKeys Value);
    public List<ModifierOption> ModifierOptions { get; } =
    [
        new("None", ModifierKeys.None),
        new("Ctrl", ModifierKeys.Control),
        new("Alt", ModifierKeys.Alt),
        new("Shift", ModifierKeys.Shift),
        new("Ctrl+Alt", ModifierKeys.Control | ModifierKeys.Alt),
        new("Ctrl+Shift", ModifierKeys.Control | ModifierKeys.Shift),
        new("Alt+Shift", ModifierKeys.Alt | ModifierKeys.Shift),
        new("Ctrl+Alt+Shift", ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)
    ];

    private void UpdatePanelWidth()
    {
        PanelWidth = IsPropertiesPinned ? PropertiesPanelWidth : 0;
    }

    private void IconBorderDragOver(object sender, DragEventArgs e)
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

    // Add this helper for XAML binding
    public string GetPlayButtonIcon(object dataContext)
    {
        if (dataContext is Audio audio)
        {
            return ViewModel.IsAudioPlaying(audio) && audio is AudioTrack ? "PauseSolid" : "PlaySolid";
        }
        return "PlaySolid";
    }

    private void RemoveAudioClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is AudioViewModel vm)
        {
            vm.RemoveAudioClick(sender, e);
        }
    }

    private void PageLoaded(object sender, RoutedEventArgs e)
    {
        if (MainWindow.Current.AudioControlPresenter.Children.Count > 0)
        {
            if (AudioControlPanel != null && AudioControlPanel.Parent is System.Windows.Controls.Panel parentPanel)
            {
                parentPanel.Children.Remove(AudioControlPanel);
            }
            MainWindow.Current.AudioControlPresenter.Children.Clear();
            AudioControlPanelParent?.Children.Add(AudioControlPanel);
        }
        e.Handled = true;
    }

    private StackPanel AudioControlPanel;
    private Grid AudioControlPanelParent;

    private void PageUnloaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsPlaying)
        {
            AudioControlPanel ??= AudioController.RightControls;
            AudioControlPanelParent ??= (Grid)AudioControlPanel.Parent;
            if (AudioControlPanel != null && AudioControlPanel.Parent is System.Windows.Controls.Panel parentPanel)
            {
                parentPanel.Children.Remove(AudioControlPanel);
            }
            MainWindow.Current.AudioControlPresenter.Children.Add(AudioControlPanel);
            AudioControlPanel.DataContext = ViewModel;
        }
        e.Handled = true;
    }

    private bool insideCanvas;

    private void VisualizationHolderMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!insideCanvas && sender is Canvas canvas)
        {
            Debug.WriteLine($"Mouse Entered: {canvas.Name}");
            insideCanvas = true;
        }
    }

    private void VisualizationHolderMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (insideCanvas && sender is Canvas canvas)
        {
            Debug.WriteLine($"Mouse Position: {e.GetPosition(canvas)}");
        }
    }

    private void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                ((System.Windows.Controls.TextBox)sender).Text = "";
                break;
        }
    }

    private void AudioListViewPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Delete:
                if (ViewModel.SelectedAudio != null)
                {
                    ViewModel.RemoveAudioCommand.Execute(ViewModel.SelectedAudio);
                }
                break;
            case Key.Enter:
                if (ViewModel.SelectedAudio != null)
                {
                    ViewModel.PlayAudioCommand.Execute(ViewModel.SelectedAudio);
                }
                break;
        }
    }



    private void AudioListView_Loaded(object sender, RoutedEventArgs e)
    {
        DefaultStyle = ViewModel.GridViewStyle;
        // Set ContextMenu DataContext directly — XAML PlacementTarget binding approaches
        // are unreliable for popup visual trees that are disconnected from the main tree.
        if (AudioListView.ContextMenu != null)
        {
            AudioListView.ContextMenu.DataContext = ViewModel;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs args)
    {
        if (sender is System.Windows.Controls.TextBox textBox)
        {
            AudioListView.Items.Filter = item =>
            {
                if (textBox.Text == "")
                {
                    return true;
                }
                if (item is Audio audio)
                {
                    return audio.Name.Contains(textBox.Text, StringComparison.InvariantCultureIgnoreCase);
                }
                return false;
            };
        }
    }

    private void FilterRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton radioButton && radioButton.Tag is string tag)
        {
            switch (tag)
            {
                case "All":
                    AppModel.Instance.Settings.FilterOptions.IncludeSoundEffects = true;
                    AppModel.Instance.Settings.FilterOptions.IncludeAudioTracks = true;
                    break;

                case "AudioTracks":
                    AppModel.Instance.Settings.FilterOptions.IncludeSoundEffects = false;
                    AppModel.Instance.Settings.FilterOptions.IncludeAudioTracks = true;
                    break;

                case "SoundEffects":
                    AppModel.Instance.Settings.FilterOptions.IncludeSoundEffects = true;
                    AppModel.Instance.Settings.FilterOptions.IncludeAudioTracks = false;
                    break;
            }
            ViewModel.AudioListCollectionView?.Refresh();
            if (AudioListView != null)
            {
                AudioListView.Items.Refresh();
            }
        }
    }
}

