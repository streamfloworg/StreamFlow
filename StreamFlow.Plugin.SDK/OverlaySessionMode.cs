namespace StreamFlow.Plugin.SDK;

/// <summary>
/// Defines how an overlay type integrates with the core compositor lifecycle.
/// </summary>
public enum OverlaySessionMode
{
    /// <summary>Rendered to static BGRA pixels and registered once; re-registered on content change.</summary>
    StaticPixels,

    /// <summary>Core manages an ongoing decode session (video, live capture).</summary>
    OngoingCoreSession,

    /// <summary>Pure compositor effect; no pixel content (blur).</summary>
    CompositorEffect,

    /// <summary>Container overlay that renders no pixels of its own (group, alert).</summary>
    Container,
}
