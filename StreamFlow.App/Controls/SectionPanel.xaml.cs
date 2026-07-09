namespace StreamFlow.App.Controls;

public partial class SectionPanel
{
    private DependencyProperty _headerTextProperty;

    public static readonly DependencyProperty HeaderTextProperty =
        DependencyProperty.Register("HeaderText", typeof(string), typeof(SectionPanel), new PropertyMetadata(default(string)));

    public string HeaderText
    {
        get { return (string)GetValue(HeaderTextProperty); }
        set { SetValue(HeaderTextProperty, value); }
    }

    public SectionPanel()
    {
        InitializeComponent();
    }
}
