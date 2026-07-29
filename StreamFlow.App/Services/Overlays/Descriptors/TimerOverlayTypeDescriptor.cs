using System.ComponentModel;
using StreamFlow.App.Rendering;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.Core.Data;

namespace StreamFlow.App.Services.Overlays.Descriptors;

public sealed class TimerOverlayTypeDescriptor : IOverlayTypeDescriptor
{
    public string TypeKey => "timer";
    public OverlayKind Kind => OverlayKind.Timer;
    public string DisplayName => "Timer Overlay";
    public string IconGlyph => "⏱️";
    public OverlaySessionMode SessionMode => OverlaySessionMode.StaticPixels;

    public IOverlayContent CreateDefault() => new TimerOverlayContent();

    public IOverlayContent? Deserialize(SlotSettings s)
    {
        var timerContent = new TimerOverlayContent
        {
            TimerMode = s.TimerMode,
            TimerDurationSeconds = s.TimerDurationSeconds,
            RawDurationText = s.TimerDurationSeconds.ToString(),
            AutoStartOnGoLive = s.TimerAutoStartOnGoLive,
        };
        ApplyTextStyleFromSettings(timerContent.Style, s);
        return timerContent;
    }

    public void Serialize(IOverlayContent content, SlotSettings s)
    {
        if (content is TimerOverlayContent timer)
        {
            s.OverlayKind = OverlayKind.Timer;
            s.TimerMode = timer.TimerMode;
            s.TimerDurationSeconds = timer.TimerDurationSeconds;
            s.TimerAutoStartOnGoLive = timer.AutoStartOnGoLive;
            SerializeTextStyle(timer.Style, s);
        }
    }

    public IOverlayContent Clone(IOverlayContent content)
    {
        if (content is TimerOverlayContent timer)
        {
            var cloned = new TimerOverlayContent
            {
                TimerMode = timer.TimerMode,
                TimerDurationSeconds = timer.TimerDurationSeconds,
                RawDurationText = timer.RawDurationText,
                AutoStartOnGoLive = timer.AutoStartOnGoLive,
            };
            CopyTextStyle(timer.Style, cloned.Style);
            return cloned;
        }
        return CreateDefault();
    }

    public (int Width, int Height, byte[] Pixels)? RenderStaticBgra(IOverlayContent content, object? slotContext)
    {
        if (content is TimerOverlayContent timer)
        {
            var display = FormatTimerDisplay(timer);
            return OverlayContentRenderer.RenderTextToBgra(display, timer.Style, slotContext as SourceSlot);
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

    public IReadOnlyList<StreamFlow.Plugin.SDK.Overlays.Sections.IOverlayPropertySection> GetInspectorSections(IOverlayContent content)
    {
        if (content is not TimerOverlayContent timer) return [];
        return [
            new StreamFlow.Plugin.SDK.Overlays.Sections.ComboSection("Timer Mode", System.Enum.GetValues<TimerMode>(), () => timer.TimerMode, v => {
                timer.TimerMode = (TimerMode)v!;
                FormatTimerDisplay(timer);
            }, timer, nameof(timer.TimerMode)),
            new StreamFlow.Plugin.SDK.Overlays.Sections.TextBoxSection("Duration (seconds)", () => timer.RawDurationText, v => {
                timer.RawDurationText = v ?? "";
                FormatTimerDisplay(timer);
            }, isMultiLine: false, source: timer, propName: nameof(timer.RawDurationText)),
            new StreamFlow.Plugin.SDK.Overlays.Sections.ToggleSection("Auto-start on Go Live", () => timer.AutoStartOnGoLive, v => timer.AutoStartOnGoLive = v, timer, nameof(timer.AutoStartOnGoLive)),
            new StreamFlow.App.Services.Overlays.Sections.TextStyleSection(timer.Style)
        ];
    }

    private static bool TryParseTimeSpanSeconds(string? input, out int seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(input))
        {
            // Allow clearing the textbox — empty string represents 0 seconds
            return true;
        }
        input = input.Trim();

        // 1. Formatted time string check (e.g. "4:00", "04:00", "1:30:00")
        if (input.Contains(':') && TimeSpan.TryParse(input, out var ts))
        {
            seconds = (int)ts.TotalSeconds;
            return true;
        }

        // 2. Plain integer seconds (e.g. "240", "300", "5")
        if (int.TryParse(input, out var secs) && secs >= 0)
        {
            seconds = secs;
            return true;
        }

        return false;
    }

    private static string FormatTimerDisplay(TimerOverlayContent timer)
    {
        var running = timer.IsTimerRunning && timer.TimerStartedAtUtc is DateTime started
            ? (DateTime.UtcNow - started).TotalSeconds
            : 0;
        var elapsed = timer.TimerElapsedBaseSeconds + running;

        var totalSeconds = timer.TimerMode == TimerMode.CountDown
            ? Math.Max(0, timer.TimerDurationSeconds - elapsed)
            : Math.Max(0, elapsed);

        var ts = TimeSpan.FromSeconds(totalSeconds);
        var text = ts.TotalHours >= 1 ? ts.ToString(@"hh\:mm\:ss") : ts.ToString(@"mm\:ss");
        timer.TimerDisplayText = text;
        return text;
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
