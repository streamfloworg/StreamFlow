using System.ComponentModel;
using StreamFlow.App.Rendering;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.Core.Data;

namespace StreamFlow.App.Services.Overlays.Descriptors;

public sealed class TextOverlayTypeDescriptor : IOverlayTypeDescriptor
{
    public string TypeKey => "text";
    public OverlayKind Kind => OverlayKind.Text;
    public string DisplayName => "Text Overlay";
    public string IconGlyph => "🔤";
    public OverlaySessionMode SessionMode => OverlaySessionMode.StaticPixels;

    public IOverlayContent CreateDefault() => new TextOverlayContent();

    public IOverlayContent? Deserialize(SlotSettings s)
    {
        var textContent = new TextOverlayContent { OverlayText = s.OverlayText };
        ApplyTextStyleFromSettings(textContent.Style, s);
        return textContent;
    }

    public void Serialize(IOverlayContent content, SlotSettings s)
    {
        if (content is TextOverlayContent text)
        {
            s.OverlayKind = OverlayKind.Text;
            s.OverlayText = text.OverlayText;
            SerializeTextStyle(text.Style, s);
        }
    }

    public IOverlayContent Clone(IOverlayContent content)
    {
        if (content is TextOverlayContent text)
        {
            var cloned = new TextOverlayContent { OverlayText = text.OverlayText };
            CopyTextStyle(text.Style, cloned.Style);
            return cloned;
        }
        return CreateDefault();
    }

    public (int Width, int Height, byte[] Pixels)? RenderStaticBgra(IOverlayContent content, SourceSlot slot)
    {
        if (content is TextOverlayContent { OverlayText: not null } text)
        {
            return OverlayContentRenderer.RenderTextToBgra(text.OverlayText, text.Style);
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
        if (content is IHasTextStyle hasStyle)
        {
            hasStyle.Style.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is not null)
                    onPropertyChanged($"Style.{e.PropertyName}");
            };
        }
    }

    private static void ApplyTextStyleFromSettings(TextStyle style, SlotSettings s)
    {
        style.FontFamily = string.IsNullOrWhiteSpace(s.TextFontFamily) ? "Segoe UI" : s.TextFontFamily;
        style.FontSize = s.TextFontSize ?? 48;
        style.FontColor = s.TextFontColorHex is not null
            && System.Windows.Media.ColorConverter.ConvertFromString(s.TextFontColorHex) is System.Windows.Media.Color parsedFontColor
            ? parsedFontColor
            : System.Windows.Media.Colors.White;
        style.IsBold = s.TextIsBold ?? true;
        style.IsItalic = s.TextIsItalic ?? false;
        style.Alignment = Enum.TryParse<TextHorizontalAlignment>(s.TextAlignment, out var alignment) ? alignment : TextHorizontalAlignment.Left;
        style.OutlineEnabled = s.TextOutlineEnabled ?? false;
        style.OutlineColor = s.TextOutlineColorHex is not null
            && System.Windows.Media.ColorConverter.ConvertFromString(s.TextOutlineColorHex) is System.Windows.Media.Color parsedOutlineColor
            ? parsedOutlineColor
            : System.Windows.Media.Colors.Black;
        style.OutlineThickness = s.TextOutlineThickness ?? 2;
    }

    private static void SerializeTextStyle(TextStyle style, SlotSettings s)
    {
        s.TextFontFamily = style.FontFamily;
        s.TextFontSize = style.FontSize;
        s.TextFontColorHex = style.FontColor.ToString();
        s.TextIsBold = style.IsBold;
        s.TextIsItalic = style.IsItalic;
        s.TextAlignment = style.Alignment.ToString();
        s.TextOutlineEnabled = style.OutlineEnabled;
        s.TextOutlineColorHex = style.OutlineColor.ToString();
        s.TextOutlineThickness = style.OutlineThickness;
    }

    private static void CopyTextStyle(TextStyle source, TextStyle target)
    {
        target.FontFamily = source.FontFamily;
        target.FontSize = source.FontSize;
        target.FontColor = source.FontColor;
        target.IsBold = source.IsBold;
        target.IsItalic = source.IsItalic;
        target.Alignment = source.Alignment;
        target.OutlineEnabled = source.OutlineEnabled;
        target.OutlineColor = source.OutlineColor;
        target.OutlineThickness = source.OutlineThickness;
    }
}
