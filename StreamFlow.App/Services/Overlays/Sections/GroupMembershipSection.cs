using System.Collections;
using StreamFlow.Plugin.SDK.Overlays.Sections;

namespace StreamFlow.App.Services.Overlays.Sections;

public sealed class GroupMembershipSection : IOverlayPropertySection
{
    public IEnumerable Candidates { get; }
    public GroupMembershipSection(IEnumerable candidates) => Candidates = candidates;
}
