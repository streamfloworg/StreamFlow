using System.ComponentModel;
using System.Runtime.CompilerServices;
using StreamFlow.Core.Data;
using StreamFlow.Plugin.SDK;

namespace StreamFlow.Plugin.Marquee;

public sealed class MarqueeOverlayContent : IOverlayContent, INotifyPropertyChanged
{
    public OverlayKind Kind => OverlayKind.Custom;

    private string _marqueeText = "★ LIVE STREAMING NOW ★ Welcome to the stream!";
    public string MarqueeText
    {
        get => _marqueeText;
        set { if (_marqueeText != value) { _marqueeText = value; OnPropertyChanged(); } }
    }

    private string _backgroundColorHex = "#FF1E1E2E";
    public string BackgroundColorHex
    {
        get => _backgroundColorHex;
        set { if (_backgroundColorHex != value) { _backgroundColorHex = value; OnPropertyChanged(); } }
    }

    private string _textColorHex = "#FFFFD700";
    public string TextColorHex
    {
        get => _textColorHex;
        set { if (_textColorHex != value) { _textColorHex = value; OnPropertyChanged(); } }
    }

    private int _fontSize = 32;
    public int FontSize
    {
        get => _fontSize;
        set { if (_fontSize != value) { _fontSize = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
