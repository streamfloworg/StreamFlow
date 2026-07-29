using System.Windows;
using System.Windows.Controls;

namespace StreamFlow.App.Controls;

public partial class SectionPanel : HeaderedContentControl
{
    public static readonly DependencyProperty HeaderTextProperty =
        DependencyProperty.Register("HeaderText", typeof(string), typeof(SectionPanel),
            new FrameworkPropertyMetadata(null, (d, e) => ((HeaderedContentControl)d).Header = e.NewValue));

    public string HeaderText
    {
        get => (string)GetValue(HeaderTextProperty);
        set => SetValue(HeaderTextProperty, value);
    }

    public SectionPanel()
    {
        HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
        InitializeComponent();
    }
}
