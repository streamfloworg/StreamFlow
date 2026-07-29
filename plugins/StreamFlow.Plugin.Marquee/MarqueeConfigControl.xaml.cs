using System.Windows.Controls;

namespace StreamFlow.Plugin.Marquee;

public partial class MarqueeConfigControl : UserControl
{
    public MarqueeConfigControl(MarqueeOverlayContent content)
    {
        InitializeComponent();
        DataContext = content;
    }
}
