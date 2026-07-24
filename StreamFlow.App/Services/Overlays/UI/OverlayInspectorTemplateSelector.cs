using System.Windows;
using System.Windows.Controls;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.Core.Data;

namespace StreamFlow.App.Services.Overlays.UI;

/// <summary>
/// Selects the appropriate WPF DataTemplate for editing a SourceSlot's Content in the Properties Panel
/// based on its OverlayKind / IOverlayTypeDescriptor.
/// </summary>
public class OverlayInspectorTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ImageTemplate { get; set; }
    public DataTemplate? TextTemplate { get; set; }
    public DataTemplate? ColorTemplate { get; set; }
    public DataTemplate? VideoTemplate { get; set; }
    public DataTemplate? ChatTemplate { get; set; }
    public DataTemplate? BlurTemplate { get; set; }
    public DataTemplate? TimerTemplate { get; set; }
    public DataTemplate? AlertTemplate { get; set; }
    public DataTemplate? GroupTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is SourceSlot slot && slot.Content is not null)
        {
            return slot.Content.Kind switch
            {
                OverlayKind.Image => ImageTemplate,
                OverlayKind.Text => TextTemplate,
                OverlayKind.Color => ColorTemplate,
                OverlayKind.Video => VideoTemplate,
                OverlayKind.Chat => ChatTemplate,
                OverlayKind.Blur => BlurTemplate,
                OverlayKind.Timer => TimerTemplate,
                OverlayKind.Alert => AlertTemplate,
                OverlayKind.Group => GroupTemplate,
                _ => base.SelectTemplate(item, container)
            };
        }
        return base.SelectTemplate(item, container);
    }
}
