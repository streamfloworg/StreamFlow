using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows;
using StreamFlow.Core.Data;
using StreamFlow.Plugin.SDK;

namespace StreamFlow.Plugin.Marquee;

public sealed class MarqueePluginDescriptor : IPluginDescriptor, IOverlayTypeDescriptor
{
    // --- IPluginDescriptor ---
    public string PluginId => "streamflow.plugin.marquee";
    public string Name => "Marquee Banner Plugin";
    public string Version => "1.0.0";
    public string Author => "StreamFlow Example Team";
    public string Description => "Example plugin providing a customizable Marquee Banner overlay.";
    public bool HasConfiguration => true;

    public FrameworkElement? CreateConfigurationControl()
    {
        return new MarqueeConfigControl((MarqueeOverlayContent)CreateDefault());
    }

    // --- IOverlayTypeDescriptor ---
    public string TypeKey => "marquee";
    public OverlayKind Kind => OverlayKind.Custom;
    public string DisplayName => "Marquee Banner";
    public string IconGlyph => "📢";
    public OverlaySessionMode SessionMode => OverlaySessionMode.StaticPixels;

    public IOverlayContent CreateDefault() => new MarqueeOverlayContent();

    public IOverlayContent? Deserialize(SlotSettings s)
    {
        var content = new MarqueeOverlayContent();
        if (s.OverlayText is not null) content.MarqueeText = s.OverlayText;
        if (s.OverlayColorHex is not null) content.BackgroundColorHex = s.OverlayColorHex;
        return content;
    }

    public void Serialize(IOverlayContent content, SlotSettings s)
    {
        if (content is MarqueeOverlayContent marquee)
        {
            s.OverlayKind = OverlayKind.Custom;
            s.OverlayTypeKey = TypeKey;
            s.OverlayText = marquee.MarqueeText;
            s.OverlayColorHex = marquee.BackgroundColorHex;
        }
    }

    public IOverlayContent Clone(IOverlayContent content)
    {
        if (content is MarqueeOverlayContent marquee)
        {
            return new MarqueeOverlayContent
            {
                MarqueeText = marquee.MarqueeText,
                BackgroundColorHex = marquee.BackgroundColorHex,
                TextColorHex = marquee.TextColorHex,
                FontSize = marquee.FontSize
            };
        }
        return CreateDefault();
    }

    public (int Width, int Height, byte[] Pixels)? RenderStaticBgra(IOverlayContent content, object? slotContext)
    {
        if (content is not MarqueeOverlayContent marquee) return null;

        int width = 1280;
        int height = 100;

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        Color bgColor;
        try { bgColor = ColorTranslator.FromHtml(marquee.BackgroundColorHex); }
        catch { bgColor = Color.FromArgb(255, 30, 30, 46); }

        Color textColor;
        try { textColor = ColorTranslator.FromHtml(marquee.TextColorHex); }
        catch { textColor = Color.FromArgb(255, 255, 215, 0); }

        // Draw background banner
        using (var bgBrush = new SolidBrush(bgColor))
        {
            g.FillRectangle(bgBrush, 0, 0, width, height);
        }

        // Draw border line
        using (var borderPen = new Pen(textColor, 3))
        {
            g.DrawRectangle(borderPen, 1, 1, width - 2, height - 2);
        }

        // Draw marquee text
        using (var font = new Font("Segoe UI", marquee.FontSize, System.Drawing.FontStyle.Bold))
        using (var textBrush = new SolidBrush(textColor))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.DrawString(marquee.MarqueeText, font, textBrush, new RectangleF(0, 0, width, height), sf);
        }

        // Convert GDI+ ARGB bitmap to BGRA byte array
        var rect = new Rectangle(0, 0, width, height);
        var bmpData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var pixels = new byte[width * height * 4];

        try
        {
            System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, pixels, 0, pixels.Length);
            // GDI+ Format32bppArgb on Windows is natively stored as BGRA byte order in memory
        }
        finally
        {
            bitmap.UnlockBits(bmpData);
        }

        return (width, height, pixels);
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

    public IReadOnlyList<StreamFlow.Plugin.SDK.Overlays.Sections.IOverlayPropertySection> GetInspectorSections(IOverlayContent content)
    {
        if (content is not MarqueeOverlayContent m) return [];
        return [
            new StreamFlow.Plugin.SDK.Overlays.Sections.GroupedSection("Marquee Settings", [
                new StreamFlow.Plugin.SDK.Overlays.Sections.TextBoxSection("Marquee Text", () => m.MarqueeText, v => m.MarqueeText = v ?? "", isMultiLine: false, source: m, propName: nameof(m.MarqueeText)),
                new StreamFlow.Plugin.SDK.Overlays.Sections.ColorSection("Background Color", () => ParseMediaColor(m.BackgroundColorHex), v => m.BackgroundColorHex = ColorToHex(v), m, nameof(m.BackgroundColorHex)),
                new StreamFlow.Plugin.SDK.Overlays.Sections.ColorSection("Text Color", () => ParseMediaColor(m.TextColorHex), v => m.TextColorHex = ColorToHex(v), m, nameof(m.TextColorHex)),
                new StreamFlow.Plugin.SDK.Overlays.Sections.SliderSection("Font Size", () => m.FontSize, v => m.FontSize = (int)v, 8, 200, 1, "{0:0}", m, nameof(m.FontSize))
            ])
        ];
    }

    private static System.Windows.Media.Color ParseMediaColor(string hex)
    {
        try
        {
            var parsed = System.Windows.Media.ColorConverter.ConvertFromString(hex);
            if (parsed is System.Windows.Media.Color c) return c;
        }
        catch { }
        return System.Windows.Media.Colors.White;
    }

    private static string ColorToHex(System.Windows.Media.Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
}
