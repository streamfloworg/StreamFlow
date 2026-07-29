using StreamFlow.App.ViewModels.Pages;
using StreamFlow.Core.Data;
using StreamFlow.Plugin.SDK.Overlays.Sections;

namespace StreamFlow.App.Services.Overlays.Sections;

public sealed class ChromaKeySection : IOverlayPropertySection
{
    public IChromaKeyable Target { get; }
    public ChromaKeySection(IChromaKeyable target) => Target = target;
}
