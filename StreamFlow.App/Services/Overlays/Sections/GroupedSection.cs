namespace StreamFlow.App.Services.Overlays.Sections;

// Re-export SDK GroupedSection so legacy StreamFlow.App.Services.Overlays.Sections.GroupedSection references work seamlessly
public class GroupedSection : StreamFlow.Plugin.SDK.Overlays.Sections.IOverlayPropertySection
{
    public string Header => _inner.Header;
    public System.Collections.Generic.IReadOnlyList<StreamFlow.Plugin.SDK.Overlays.Sections.IOverlayPropertySection> Sections => _inner.Sections;

    private readonly StreamFlow.Plugin.SDK.Overlays.Sections.GroupedSection _inner;

    public GroupedSection(string header, System.Collections.Generic.IReadOnlyList<StreamFlow.Plugin.SDK.Overlays.Sections.IOverlayPropertySection> sections)
    {
        _inner = new StreamFlow.Plugin.SDK.Overlays.Sections.GroupedSection(header, sections);
    }
}
