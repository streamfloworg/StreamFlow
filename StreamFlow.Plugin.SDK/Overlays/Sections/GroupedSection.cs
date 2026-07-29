using System.Collections.Generic;

namespace StreamFlow.Plugin.SDK.Overlays.Sections;

/// <summary>
/// A section that groups multiple child sections inside a titled SectionPanel card.
/// </summary>
public sealed class GroupedSection : IOverlayPropertySection
{
    public string Header { get; }
    public IReadOnlyList<IOverlayPropertySection> Sections { get; }

    public GroupedSection(string header, IReadOnlyList<IOverlayPropertySection> sections)
    {
        Header = header;
        Sections = sections;
    }
}
