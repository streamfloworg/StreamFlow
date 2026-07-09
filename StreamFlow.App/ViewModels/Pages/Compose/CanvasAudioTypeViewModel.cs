using StreamFlow.App.Models.Canvas;
using StreamFlow.Core.AudioHandling;

using Color = System.Windows.Media.Color;
using MediaBrush = System.Windows.Media.Brush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;


namespace StreamFlow.App.ViewModels.Pages.Compose;


public partial class CanvasAudioTypeViewModel : ObservableObject, ICanvasPosition
{
    [ObservableProperty]
    private double row;

    [ObservableProperty]
    private double x;

    [ObservableProperty]
    private double y;

    public double Top
    {
        get => Y;
        set => Y = value;
    }
    public double Left
    {
        get => X;
        set => X = value;
    }

    [ObservableProperty]
    private double width = 160;

    [ObservableProperty]
    private double height = 96;

    [ObservableProperty]
    private string displayName;

    [ObservableProperty]
    private Audio audioItem;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool isSelectable = true;

    [ObservableProperty]
    private bool allowScaleChangeToUpdatePosition = true;

    [ObservableProperty]
    private bool isDraggable = true;

    [ObservableProperty]
    private bool isDragging = false;

    [ObservableProperty]
    private Visibility _visibility = Visibility.Visible;

    [ObservableProperty]
    private bool _isHitTestable = true;

    public CanvasAudioTypeViewModel(CanvasAudioKind kind, Audio audioItem = default)
    {
        Kind = kind;
        AudioItem = audioItem;
        DisplayName = $"{Kind} - {AudioItem?.Name}";
        if (Kind == CanvasAudioKind.Clone)
        {
            IsHitTestable = false;
        }
        else
        {
            IsHitTestable = true;
        }
    }

    public CanvasAudioKind Kind
    {
        get;
    }

    public double MinWidth => 100;

    public double MinHeight { get; private set; } = 96;

    public void SetMinHeight(double newMinHeight)
    {
        MinHeight = newMinHeight;
        Height = MinHeight;
    }
}
