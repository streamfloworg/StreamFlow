using System.Collections.ObjectModel;
using System.Collections.Specialized;

using StreamFlow.App.Models.Canvas;
using StreamFlow.App.ViewModels.Pages.Compose;

namespace StreamFlow.App.ViewModels.Pages;

using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

public partial class ComposeViewModel : ViewModel
{
    private readonly Random random = new();
    private CanvasAudioTypeViewModel? selectedType;

    private CanvasAudioTypeViewModel _clone;

    public CanvasAudioTypeViewModel Clone {
        get => _clone;
        set
        {
            _clone ??= CreateType(CanvasAudioKind.Clone);
            _clone.X = value.X;
            _clone.Y = value.Y;
            _clone.Width = value.Width;
            _clone.Height = value.Height;
            _clone.Row = value.Row;
        }
    }

    public ComposeViewModel()
    {
        Types.CollectionChanged += OnAudioCollectionChanged;
    }

    public ObservableCollection<CanvasAudioTypeViewModel> Types { get; } = [];

    [ObservableProperty]
    private double _gridSize = 96;

    [ObservableProperty]
    private double canvasZoom = 1.0;

    [ObservableProperty]
    private List<CanvasAudioTypeViewModel> _selectedTypes = [];

    public CanvasAudioTypeViewModel? SelectedType
    {
        get => selectedType;
        set
        {
            selectedType = value;
        }
    }

    [RelayCommand]
    private void AddAudioTrack() => AddType(CanvasAudioKind.AudioTrack);

    [RelayCommand]
    private void AddSoundEffect() => AddType(CanvasAudioKind.SoundEffect);

    [RelayCommand]
    private void SelectType(CanvasAudioTypeViewModel? type) => SelectedType = type;

    [RelayCommand]
    private void ClearSelection() => SelectedType = null;

    private void AddType(CanvasAudioKind kind)
    {
        var type = CreateType(kind);
        type.SetMinHeight(GridSize);
        Types.Add(type);
        SelectType(type);
    }

    private CanvasAudioTypeViewModel CreateType(CanvasAudioKind kind, double minHeight = 96)
    {
        var type = new CanvasAudioTypeViewModel(kind)
        {
            DisplayName = $"{kind} {Types.Count(existing => existing.Kind == kind) + 1}",
        };

        switch (kind)
        {
            case CanvasAudioKind.AudioTrack:
                type.Width = type.MinWidth;
                type.Height = type.MinHeight;
                break;
            case CanvasAudioKind.SoundEffect:
                type.Width = type.MinWidth;
                type.Height = type.MinHeight;
                break;
            case CanvasAudioKind.Clone:
                type.Width = type.MinWidth;
                type.Height = type.MinHeight;
                type.Visibility = Visibility.Collapsed;
                type.X = Random.Shared.Next(10, 100);
                type.Y = Random.Shared.Next(10, 100);
                break;
        }
        if (kind != CanvasAudioKind.Clone)
        {
            var color = Color.FromRgb(
                (byte)random.Next(60, 200),
                (byte)random.Next(60, 200),
                (byte)random.Next(60, 200));

            var fill = new SolidColorBrush(color);
            if (fill.CanFreeze)
            {
                fill.Freeze();
            }

            var (x, y) = SnapToRowAndTime(random.Next(10, 100), random.Next(1, 15)); //

            type.X = x;
            type.Y = y;
        }
        return type;
    }

    

    private (double Time, double Row) SnapToRowAndTime(double time, double row)
    {
        var snapRow = Math.Round((row / GridSize) * GridSize, 2);

        return (time, snapRow);
    }

    private (double X, double Y) SnapToGrid(double x, double y)
    {
        var size = Math.Max(8d, GridSize);
        var snapX = Math.Round(x / size) * size;
        var snapY = Math.Round(y / size) * size;

        return (snapX, snapY);
    }

    private void OnAudioCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Remove || e.Action == NotifyCollectionChangedAction.Reset)
        {
            if (SelectedType is null)
            {
                return;
            }

            var stillPresent = Types.Contains(SelectedType);
            if (!stillPresent)
            {
                SelectedType = Types.LastOrDefault();
            }
        }
    }
}
