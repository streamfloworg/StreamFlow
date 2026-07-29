using StreamFlow.Core.Data;

namespace StreamFlow.Plugin.SDK;

/// <summary>
/// Contract for overlay type descriptors in the Overlay API.
/// Concrete descriptors own creation, serialization, rendering, and property hooks for an overlay kind.
/// </summary>
public interface IOverlayTypeDescriptor
{
    /// <summary>Unique stable type key matching the core wire prefix (e.g., "image", "text", "color", "video", "chat", "blur", "timer", "group", "alert", or custom plugin key).</summary>
    string TypeKey { get; }

    /// <summary>Corresponding <see cref="OverlayKind"/> enum value.</summary>
    OverlayKind Kind { get; }

    /// <summary>Display name shown in UI add/create menus.</summary>
    string DisplayName { get; }

    /// <summary>Icon glyph or identifier for UI layer lists.</summary>
    string IconGlyph { get; }

    /// <summary>Integration session mode with the core compositor.</summary>
    OverlaySessionMode SessionMode { get; }

    /// <summary>Creates a default content instance for new slots of this kind.</summary>
    IOverlayContent CreateDefault();

    /// <summary>Restores a content instance from persisted slot settings.</summary>
    IOverlayContent? Deserialize(SlotSettings s);

    /// <summary>Writes content properties back to a SlotSettings instance for persistence.</summary>
    void Serialize(IOverlayContent content, SlotSettings s);

    /// <summary>Deep-copies a content instance for scene/slot duplication.</summary>
    IOverlayContent Clone(IOverlayContent content);

    /// <summary>Renders static BGRA pixel buffer for the core pipe. Returns null if not static pixel based.</summary>
    (int Width, int Height, byte[] Pixels)? RenderStaticBgra(IOverlayContent content, object? slotContext);

    /// <summary>Hooks property change listeners on content.</summary>
    void HookPropertyChanges(IOverlayContent content, Action<string> onPropertyChanged);

    /// <summary>Returns property inspector sections for configuring this overlay kind in UI panels.</summary>
    IReadOnlyList<StreamFlow.Plugin.SDK.Overlays.Sections.IOverlayPropertySection> GetInspectorSections(IOverlayContent content);
}
