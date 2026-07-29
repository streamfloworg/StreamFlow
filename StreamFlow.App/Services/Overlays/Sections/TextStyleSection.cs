using StreamFlow.App.ViewModels.Pages;
using StreamFlow.Core.Data;
using StreamFlow.Plugin.SDK.Overlays.Sections;

namespace StreamFlow.App.Services.Overlays.Sections;

public sealed class TextStyleSection : IOverlayPropertySection
{
    public TextStyle Style { get; }
    public TextStyleSection(TextStyle style) => Style = style;
}
