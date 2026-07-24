using System.ComponentModel;
using StreamFlow.App.Rendering;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.Core.Data;

namespace StreamFlow.App.Services.Overlays.Descriptors;

public sealed class ImageOverlayTypeDescriptor : IOverlayTypeDescriptor
{
    public string TypeKey => "image";
    public OverlayKind Kind => OverlayKind.Image;
    public string DisplayName => "Image Overlay";
    public string IconGlyph => "🖼️";
    public OverlaySessionMode SessionMode => OverlaySessionMode.StaticPixels;

    public IOverlayContent CreateDefault() => new ImageOverlayContent();

    public IOverlayContent? Deserialize(SlotSettings s)
    {
        var chromaColor = s.ChromaKeyColorHex is not null
            && System.Windows.Media.ColorConverter.ConvertFromString(s.ChromaKeyColorHex) is System.Windows.Media.Color parsedColor
            ? parsedColor
            : System.Windows.Media.Color.FromRgb(0x00, 0xB1, 0x40);

        return new ImageOverlayContent
        {
            ImagePath = s.ImagePath,
            ChromaKeyEnabled = s.ChromaKeyEnabled,
            ChromaKeySimilarity = s.ChromaKeySimilarity,
            ChromaKeyColor = chromaColor
        };
    }

    public void Serialize(IOverlayContent content, SlotSettings s)
    {
        if (content is ImageOverlayContent img)
        {
            s.OverlayKind = OverlayKind.Image;
            s.ImagePath = img.ImagePath;
            s.ChromaKeyEnabled = img.ChromaKeyEnabled;
            s.ChromaKeySimilarity = img.ChromaKeySimilarity;
            s.ChromaKeyColorHex = img.ChromaKeyColor.ToString();
        }
    }

    public IOverlayContent Clone(IOverlayContent content)
    {
        if (content is ImageOverlayContent img)
        {
            return new ImageOverlayContent
            {
                ImagePath = img.ImagePath,
                ChromaKeyEnabled = img.ChromaKeyEnabled,
                ChromaKeySimilarity = img.ChromaKeySimilarity,
                ChromaKeyColor = img.ChromaKeyColor
            };
        }
        return CreateDefault();
    }

    public (int Width, int Height, byte[] Pixels)? RenderStaticBgra(IOverlayContent content, SourceSlot slot)
    {
        if (content is ImageOverlayContent { ImagePath: not null } image)
        {
            // Cap headroom logic is preserved via OverlayContentRenderer or slot max cap
            return OverlayContentRenderer.DecodeImageToBgra(image.ImagePath);
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
