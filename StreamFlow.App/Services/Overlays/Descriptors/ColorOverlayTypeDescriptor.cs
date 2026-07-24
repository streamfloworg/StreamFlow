using System.ComponentModel;
using StreamFlow.App.Rendering;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.Core.Data;

namespace StreamFlow.App.Services.Overlays.Descriptors;

public sealed class ColorOverlayTypeDescriptor : IOverlayTypeDescriptor
{
    public string TypeKey => "color";
    public OverlayKind Kind => OverlayKind.Color;
    public string DisplayName => "Color Box";
    public string IconGlyph => "🎨";
    public OverlaySessionMode SessionMode => OverlaySessionMode.StaticPixels;

    public IOverlayContent CreateDefault() => new ColorOverlayContent();

    public IOverlayContent? Deserialize(SlotSettings s)
    {
        var overlayColor = s.OverlayColorHex is not null
            ? System.Windows.Media.ColorConverter.ConvertFromString(s.OverlayColorHex) as System.Windows.Media.Color?
            : null;

        return new ColorOverlayContent { OverlayColor = overlayColor };
    }

    public void Serialize(IOverlayContent content, SlotSettings s)
    {
        if (content is ColorOverlayContent color)
        {
            s.OverlayKind = OverlayKind.Color;
            s.OverlayColorHex = color.OverlayColor?.ToString();
        }
    }

    public IOverlayContent Clone(IOverlayContent content)
    {
        if (content is ColorOverlayContent color)
        {
            return new ColorOverlayContent { OverlayColor = color.OverlayColor };
        }
        return CreateDefault();
    }

    public (int Width, int Height, byte[] Pixels)? RenderStaticBgra(IOverlayContent content, SourceSlot slot)
    {
        if (content is ColorOverlayContent { OverlayColor: System.Windows.Media.Color color })
        {
            return OverlayContentRenderer.RenderColorToBgra(color);
        }
        return null;
    }

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
