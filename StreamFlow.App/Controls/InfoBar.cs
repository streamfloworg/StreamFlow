using System.Windows;
using System.Windows.Controls;

namespace StreamFlow.App.Controls;



public class FontIconSource
{
    public string? Glyph { get; set; }
    public object? FontFamily { get; set; }
}

public class InfoBar : ContentControl
{
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        "IsOpen", typeof(bool), typeof(InfoBar), new PropertyMetadata(false, OnIsOpenChanged));

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public object? IconSource { get; set; }
    public bool IsClosable { get; set; }
    public InfoBarSeverity Severity { get; set; }
    public CornerRadius CornerRadius { get; set; }

    public InfoBar()
    {
        Loaded += (s, e) =>
        {
            var border = new Border
            {
                Background = this.Background ?? System.Windows.Media.Brushes.DarkSlateGray,
                BorderBrush = System.Windows.Media.Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = this.CornerRadius,
                Padding = new Thickness(12, 8, 12, 8),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = Title, FontWeight = FontWeights.Bold, FontSize = 12, Foreground = System.Windows.Media.Brushes.White },
                        new TextBlock { Text = Message, FontSize = 11, Foreground = System.Windows.Media.Brushes.LightGray, Margin = new Thickness(0, 4, 0, 0) }
                    }
                }
            };
            this.Content = border;
        };
    }

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InfoBar bar && !(bool)e.NewValue)
        {
            if (bar.Parent is System.Windows.Controls.Panel panel)
            {
                panel.Children.Remove(bar);
            }
        }
    }
}
