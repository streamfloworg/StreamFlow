using System.Collections.Concurrent;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.Core.Data;

namespace StreamFlow.App.Services.Overlays;

/// <summary>
/// Central registry for all registered <see cref="IOverlayTypeDescriptor"/> instances.
/// Serves as the single source of truth for overlay types across serialization, rendering, and UI.
/// </summary>
public sealed class OverlayTypeRegistry
{
    private readonly ConcurrentDictionary<string, IOverlayTypeDescriptor> _byKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<OverlayKind, IOverlayTypeDescriptor> _byKind = new();
    private readonly ConcurrentDictionary<Type, IOverlayTypeDescriptor> _byContentType = new();

    public void Register(IOverlayTypeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _byKey[descriptor.TypeKey] = descriptor;
        _byKind[descriptor.Kind] = descriptor;

        var defaultContent = descriptor.CreateDefault();
        if (defaultContent is not null)
        {
            _byContentType[defaultContent.GetType()] = descriptor;
        }
    }

    public void Unregister(IOverlayTypeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _byKey.TryRemove(descriptor.TypeKey, out _);
        if (_byKind.TryGetValue(descriptor.Kind, out var existingKind) && ReferenceEquals(existingKind, descriptor))
        {
            _byKind.TryRemove(descriptor.Kind, out _);
        }
        var defaultContent = descriptor.CreateDefault();
        if (defaultContent is not null)
        {
            _byContentType.TryRemove(defaultContent.GetType(), out _);
        }
    }

    public IOverlayTypeDescriptor? GetByKey(string? typeKey) =>
        !string.IsNullOrEmpty(typeKey) && _byKey.TryGetValue(typeKey, out var d) ? d : null;

    public IOverlayTypeDescriptor? GetByKind(OverlayKind kind) =>
        _byKind.TryGetValue(kind, out var d) ? d : null;

    public IOverlayTypeDescriptor? GetForContent(IOverlayContent? content) =>
        content is not null && _byContentType.TryGetValue(content.GetType(), out var d) ? d : null;

    public IEnumerable<IOverlayTypeDescriptor> All => _byKey.Values;
}
