using System.ComponentModel;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.Core.Data;

namespace StreamFlow.App.Services.Overlays.Descriptors;

public sealed class BlurOverlayTypeDescriptor : IOverlayTypeDescriptor
{
    public string TypeKey => "blur";
    public OverlayKind Kind => OverlayKind.Blur;
    public string DisplayName => "Blur Layer";
    public string IconGlyph => "🌫️";
    public OverlaySessionMode SessionMode => OverlaySessionMode.CompositorEffect;

    public IOverlayContent CreateDefault() => new BlurOverlayContent();

    public IOverlayContent? Deserialize(SlotSettings s)
    {
        return new BlurOverlayContent { BlurRadius = s.BlurRadius };
    }

    public void Serialize(IOverlayContent content, SlotSettings s)
    {
        if (content is BlurOverlayContent blur)
        {
            s.OverlayKind = OverlayKind.Blur;
            s.BlurRadius = blur.BlurRadius;
        }
    }

    public IOverlayContent Clone(IOverlayContent content)
    {
        if (content is BlurOverlayContent blur)
        {
            return new BlurOverlayContent { BlurRadius = blur.BlurRadius };
        }
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
