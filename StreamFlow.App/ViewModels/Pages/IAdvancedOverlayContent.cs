using System.Collections.ObjectModel;

namespace StreamFlow.App.ViewModels.Pages;

/// <summary>
/// Abstraction for advanced/complex overlays (such as Stream Alerts, Goal Widgets, Slideshows)
/// that natively manage their own internal sub-layers rather than behaving like standard
/// UI TreeView groups.
/// </summary>
public interface IAdvancedOverlayContent : IOverlayContent
{
    ObservableCollection<SourceSlot> SubLayers { get; }
}
