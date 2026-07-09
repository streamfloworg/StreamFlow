using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using StreamFlow.App.Services;
using StreamFlow.App.ViewModels.Pages;

namespace StreamFlow.App.Rendering;

/// <summary>Pure overlay-content rendering (decode/rasterize to straight-alpha BGRA) — every
/// method here is a pure function of its inputs with no ViewModel state dependency.</summary>
public static class OverlayContentRenderer
{
    /// <summary>Native size doesn't matter for a solid fill — a small buffer scales losslessly
    /// to whatever box the user sets, so this stays fixed regardless of placement.</summary>
    private const int ColorOverlaySize = 8;

    /// <summary>Applies the same color-key masking the Rust compositor's chroma_mask/smoothstep
    /// do (native/crates/core/src/compositor.rs), in place on a straight-alpha BGRA buffer — used
    /// to give the local editor canvas a WYSIWYG preview of chromakey without a GPU shader.
    /// <paramref name="similarityPercent"/> is 0-100, matching SourceSlot.ChromaKeySimilarity's
    /// UI scale.</summary>
    public static void ApplyChromaKey(byte[] bgraPixels, System.Windows.Media.Color keyColor, double similarityPercent)
    {
        var similarity = (float)(similarityPercent / 100.0);
        for (var i = 0; i + 3 < bgraPixels.Length; i += 4)
        {
            var a = bgraPixels[i + 3];
            if (a == 0) continue;

            var mask = ChromaMask(bgraPixels[i + 2], bgraPixels[i + 1], bgraPixels[i], keyColor.R, keyColor.G, keyColor.B, similarity);
            bgraPixels[i + 3] = (byte)Math.Clamp(a * mask, 0f, 255f);
        }
    }

    private static float ChromaMask(byte sr, byte sg, byte sb, byte keyR, byte keyG, byte keyB, float similarity)
    {
        var dr = sr - (float)keyR;
        var dg = sg - (float)keyG;
        var db = sb - (float)keyB;
        var distSq = (dr * dr + dg * dg + db * db) / (3f * 255f * 255f);

        var lo = similarity * similarity;
        var hi = (similarity + 0.12f) * (similarity + 0.12f);
        return Smoothstep(lo, hi, distSq);
    }

    private static float Smoothstep(float lo, float hi, float x)
    {
        if (hi <= lo) return x < lo ? 0f : 1f;
        var t = Math.Clamp((x - lo) / (hi - lo), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    public static (int Width, int Height, byte[] Pixels) RenderColorToBgra(System.Windows.Media.Color color)
    {
        var pixels = new byte[ColorOverlaySize * ColorOverlaySize * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = color.A;
        }
        return (ColorOverlaySize, ColorOverlaySize, pixels);
    }

    /// <summary>Applies a native pixel size to a slot's aspect ratio immediately — no round
    /// trip to the core is needed for a static overlay, unlike capture sources whose resolution
    /// only the core can determine.</summary>
    public static void ApplyRenderedAspectRatio(SourceSlot slot, int width, int height)
    {
        if (width <= 0 || height <= 0) return;

        slot.AspectRatio = width / (double)height;
        if (slot.IsAspectLocked)
            slot.ResizeToWidthPercent(slot.WPercent);
    }

    /// <summary>Headroom over an image overlay's current on-screen pixel size when capping its
    /// decode resolution — enough to stay sharp if the box is enlarged a bit afterward, without
    /// registering (and re-scaling, and shipping over IPC) an arbitrarily large source buffer
    /// for content that might only ever render as a small corner logo. Much smaller than
    /// <see cref="TextSupersampleFactor"/> since this is a quality margin, not supersampling.</summary>
    public const double ImageOverlayCapHeadroom = 2.0;

    /// <summary>Decodes an image file to straight-alpha BGRA — the format the compositor's
    /// alpha-blend expects (matching how it already treats raw capture frames). Downscales at
    /// decode time if the source file is larger than <paramref name="maxSize"/> (with headroom
    /// already applied by the caller) — never upscales past the file's native resolution, since
    /// the compositor's own scaler handles final on-screen fit regardless.</summary>
    public static (int Width, int Height, byte[] Pixels)? DecodeImageToBgra(string imagePath, (int Width, int Height)? maxSize = null)
    {
        try
        {
            using var stream = File.OpenRead(imagePath);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            BitmapSource source = decoder.Frames[0];

            if (maxSize is var (maxW, maxH) && (source.PixelWidth > maxW || source.PixelHeight > maxH))
            {
                var scale = Math.Min((double)maxW / source.PixelWidth, (double)maxH / source.PixelHeight);
                source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            }

            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

            var width = converted.PixelWidth;
            var height = converted.PixelHeight;
            if (width <= 0 || height <= 0) return null;

            var stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);
            return (width, height, pixels);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>Rendered several times larger than a typical on-screen size and then downscaled
    /// by the compositor to fit wherever the overlay box actually ends up — supersampling, so
    /// edges stay smooth through that resize instead of the compositor's bilinear filter
    /// upscaling a low-detail source (which is what produced the jagged edges on stream: text
    /// looked fine locally because WPF renders it directly at display size, but the bitmap
    /// actually sent to the core was too small for however large the box was placed).</summary>
    private const double TextSupersampleFactor = 4.0;

    /// <summary>Rasterizes a string to a tightly-cropped BGRA bitmap (white bold text on a
    /// transparent background) for use as a static overlay.</summary>
    public static (int Width, int Height, byte[] Pixels)? RenderTextToBgra(
        string text, double fontSize = 48, System.Windows.Media.Color? color = null, bool bold = true)
    {
        var typeface = new Typeface(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        var formatted = new FormattedText(
            text, CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight, typeface,
            emSize: fontSize * TextSupersampleFactor, new SolidColorBrush(color ?? System.Windows.Media.Colors.White), 1.0);

        var width = (int)Math.Ceiling(formatted.WidthIncludingTrailingWhitespace);
        var height = (int)Math.Ceiling(formatted.Height);
        if (width <= 0 || height <= 0) return null;

        var visual = new DrawingVisual();
        // RenderTargetBitmap doesn't apply ClearType (there's no real subpixel geometry off
        // screen), but explicitly force grayscale antialiasing rather than leaving it to
        // whatever the default happens to resolve to.
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
        TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Ideal);
        using (var dc = visual.RenderOpen())
            dc.DrawText(formatted, new System.Windows.Point(0, 0));

        // RenderTargetBitmap only renders in premultiplied alpha — un-premultiply below so this
        // matches the straight alpha the compositor's alpha-blend expects everywhere else.
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var stride = width * 4;
        var pixels = new byte[stride * height];
        target.CopyPixels(pixels, stride, 0);

        for (var i = 0; i < pixels.Length; i += 4)
        {
            var a = pixels[i + 3];
            if (a is 0 or 255) continue;
            pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / a);
            pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / a);
            pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / a);
        }

        return (width, height, pixels);
    }

    public static (int Width, int Height, byte[] Pixels)? RenderChatToBgra(SourceSlot slot)
    {
        var targetWidth = (int)Math.Ceiling((slot.WPercent / 100.0 * 640) * TextSupersampleFactor);
        var targetHeight = (int)Math.Ceiling((slot.HPercent / 100.0 * 360) * TextSupersampleFactor);

        if (targetWidth <= 0 || targetHeight <= 0)
            return RenderEmptyChatToBgra();

        var messages = slot.Content is ChatOverlayContent chat ? (IReadOnlyList<ChatMessage>)chat.ChatMessages : Array.Empty<ChatMessage>();
        if (messages.Count == 0)
        {
            var emptyPixels = new byte[targetWidth * targetHeight * 4];
            return (targetWidth, targetHeight, emptyPixels);
        }

        var typeface = new Typeface(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var spacing = 8 * TextSupersampleFactor;
        var messagesToRender = new List<FormattedText>();
        double totalHeight = 0;

        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            var text = $"{msg.Username}: {msg.Text}";
            var formatted = new FormattedText(
                text, CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight, typeface,
                emSize: 16 * TextSupersampleFactor, System.Windows.Media.Brushes.White, 1.0)
            {
                MaxTextWidth = targetWidth
            };

            var usernameLength = msg.Username.Length + 1; // username + colon
            formatted.SetFontWeight(FontWeights.Bold, 0, usernameLength);

            System.Windows.Media.Color color = System.Windows.Media.Color.FromRgb(0, 200, 255);
            if (msg.ColorHex is not null && TryParseHtmlColor(msg.ColorHex, out var parsedColor))
            {
                color = parsedColor;
            }
            formatted.SetForegroundBrush(new SolidColorBrush(color), 0, usernameLength);

            formatted.SetFontWeight(FontWeights.Normal, usernameLength, msg.Text.Length + 1);
            formatted.SetForegroundBrush(System.Windows.Media.Brushes.White, usernameLength, msg.Text.Length + 1);

            var nextHeight = totalHeight + formatted.Height + (messagesToRender.Count > 0 ? spacing : 0);
            if (nextHeight > targetHeight)
            {
                break;
            }

            messagesToRender.Insert(0, formatted);
            totalHeight = nextHeight;
        }

        var pixels = new byte[targetWidth * targetHeight * 4];
        var visual = new DrawingVisual();
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
        TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Ideal);
        using (var dc = visual.RenderOpen())
        {
            double y = targetHeight - totalHeight;
            foreach (var formatted in messagesToRender)
            {
                dc.DrawText(formatted, new System.Windows.Point(0, y));
                y += formatted.Height + spacing;
            }
        }

        var target = new RenderTargetBitmap(targetWidth, targetHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var stride = targetWidth * 4;
        target.CopyPixels(pixels, stride, 0);

        for (var i = 0; i < pixels.Length; i += 4)
        {
            var a = pixels[i + 3];
            if (a is 0 or 255) continue;
            pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / a);
            pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / a);
            pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / a);
        }

        return (targetWidth, targetHeight, pixels);
    }

    public static (int Width, int Height, byte[] Pixels) RenderEmptyChatToBgra()
    {
        var pixels = new byte[8 * 8 * 4];
        return (8, 8, pixels);
    }

    private static bool TryParseHtmlColor(string hex, out System.Windows.Media.Color color)
    {
        color = System.Windows.Media.Colors.White;
        try
        {
            var parsed = System.Windows.Media.ColorConverter.ConvertFromString(hex);
            if (parsed is System.Windows.Media.Color c)
            {
                color = c;
                return true;
            }
        }
        catch { }
        return false;
    }
}
