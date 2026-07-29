using StreamFlow.App.ViewModels.Pages;
using StreamFlow.Plugin.SDK.Overlays.Sections;

namespace StreamFlow.App.Services.Overlays.Sections;

public class SubLayerLayoutSection : IOverlayPropertySection
{
    public SourceSlot SubSlot { get; }

    public SubLayerLayoutSection(SourceSlot subSlot)
    {
        SubSlot = subSlot;
    }
}
