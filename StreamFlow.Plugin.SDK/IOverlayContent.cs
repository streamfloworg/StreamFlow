using StreamFlow.Core.Data;

namespace StreamFlow.Plugin.SDK;

/// <summary>
/// Base contract for overlay content objects.
/// </summary>
public interface IOverlayContent
{
    OverlayKind Kind { get; }
}
