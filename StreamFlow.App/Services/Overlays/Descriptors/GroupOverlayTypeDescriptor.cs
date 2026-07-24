using System.ComponentModel;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.Core.Data;

namespace StreamFlow.App.Services.Overlays.Descriptors;

public sealed class GroupOverlayTypeDescriptor : IOverlayTypeDescriptor
{
    public string TypeKey => "group";
    public OverlayKind Kind => OverlayKind.Group;
    public string DisplayName => "Overlay Group";
    public string IconGlyph => "📁";
    public OverlaySessionMode SessionMode => OverlaySessionMode.Container;

    public IOverlayContent CreateDefault() => new GroupOverlayContent();

    public IOverlayContent? Deserialize(SlotSettings s)
    {
        return new GroupOverlayContent();
    }

    public void Serialize(IOverlayContent content, SlotSettings s)
    {
        if (content is GroupOverlayContent group)
        {
            s.OverlayKind = OverlayKind.Group;
            s.GroupChildIds = group.Children.Select(c => c.SourceId).Where(id => id is not null).ToList()!;
        }
    }

    public IOverlayContent Clone(IOverlayContent content)
    {
        return CreateDefault();
    }

    public (int Width, int Height, byte[] Pixels)? RenderStaticBgra(IOverlayContent content, SourceSlot slot) => null;

    public void HookPropertyChanges(IOverlayContent content, Action<string> onPropertyChanged)
    {
        if (content is INotifyPropertyChanged notifying)
        {
            notifying.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is not null)
                    onPropertyChanged(e.PropertyName);
            };
        }
    }
}
