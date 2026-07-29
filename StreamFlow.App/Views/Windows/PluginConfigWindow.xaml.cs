using System.Windows;
using StreamFlow.App.Services.Overlays.Plugins;

namespace StreamFlow.App.Views.Windows;

public partial class PluginConfigWindow : Window
{
    public PluginConfigWindow(PluginInfo plugin)
    {
        InitializeComponent();
        TitleText.Text = $"{plugin.Name} Configuration";

        if (plugin.Descriptor?.CreateConfigurationControl() is FrameworkElement configControl)
        {
            ConfigContentControl.Content = configControl;
        }
        else
        {
            ConfigContentControl.Content = new System.Windows.Controls.TextBlock
            {
                Text = "No additional configuration UI provided by this plugin.",
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
