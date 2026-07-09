using System.Collections.ObjectModel;
using System.Windows.Media;

using StreamFlow.App.Services;

namespace StreamFlow.App.ViewModels.Pages;

/// <summary>Kind-specific payload for an overlay <see cref="SourceSlot"/> — everything that
/// differs between the seven overlay kinds lives in one of these instead of as loose nullable
/// properties directly on SourceSlot (which otherwise has to be a single flat bag of fields for
/// every kind at once). SourceSlot keeps only what every kind shares (position/size/rotation/
/// opacity/etc.) plus this one Content property; <see cref="SourceSlot.OverlayKind"/> is derived
/// from which concrete type Content actually is, rather than a separately-tracked field that
/// could drift out of sync with it.</summary>
public interface IOverlayContent
{
    OverlayKind Kind { get; }
}

/// <summary>Shared by the two overlay kinds that support color-key transparency. The compositor
/// itself doesn't care what kind of layer it's keying — this is purely a UI-exposure grouping
/// (see <see cref="SourceSlot.SupportsChromaKey"/>), not a compositor-level distinction.</summary>
public interface IChromaKeyable : IOverlayContent
{
    bool ChromaKeyEnabled { get; set; }
    System.Windows.Media.Color ChromaKeyColor { get; set; }
    double ChromaKeySimilarity { get; set; }
}

public partial class ImageOverlayContent : ObservableObject, IChromaKeyable
{
    public OverlayKind Kind => OverlayKind.Image;

    /// <summary>Source file path. Settable (not just init) so the properties panel can
    /// re-browse to a different image — GoLiveViewModel reacts to the change by re-rendering
    /// and re-registering the overlay's pixels.</summary>
    [ObservableProperty]
    private string? _imagePath;

    /// <summary>A chroma-keyed render of ImagePath, shown instead of the plain path while
    /// ChromaKeyEnabled is set — gives the local editor canvas the same WYSIWYG chromakey
    /// preview the real composited/stream output gets. Recomputed by SceneEditorViewModel.
    /// UpdateChromaKeyPreview whenever the image, key color, or similarity changes.</summary>
    [ObservableProperty]
    private ImageSource? _keyedPreviewSource;

    [ObservableProperty]
    private bool _chromaKeyEnabled;

    /// <summary>Defaults to a standard chroma green so picking a color is optional for the
    /// common case.</summary>
    [ObservableProperty]
    private System.Windows.Media.Color _chromaKeyColor = System.Windows.Media.Color.FromRgb(0x00, 0xB1, 0x40);

    /// <summary>0-100 UI scale for how close to ChromaKeyColor a pixel must be to get keyed
    /// out — converted to the compositor's 0.0-1.0 normalized threshold in
    /// SceneEditorViewModel.BuildStreamSources.</summary>
    [ObservableProperty]
    private double _chromaKeySimilarity = 40;
}

public partial class TextOverlayContent : ObservableObject, IOverlayContent
{
    public OverlayKind Kind => OverlayKind.Text;

    /// <summary>Settable so the properties panel can edit it live — GoLiveViewModel
    /// re-renders and re-registers the overlay's pixels (debounced) when this changes.</summary>
    [ObservableProperty]
    private string? _overlayText;

    /// <summary>Point size the text renders at — independent of the containing slot's own
    /// W/H box (which only controls where the rendered text sits and how large a box it's
    /// placed in via the aspect-locked auto-resize, not the font size itself). Matches the
    /// pre-refactor hardcoded default in OverlayContentRenderer.RenderTextToBgra.</summary>
    [ObservableProperty]
    private double _fontSize = 48;

    [ObservableProperty]
    private System.Windows.Media.Color _fontColor = System.Windows.Media.Colors.White;

    [ObservableProperty]
    private bool _isBold = true;
}

public partial class ColorOverlayContent : ObservableObject, IOverlayContent
{
    public OverlayKind Kind => OverlayKind.Color;

    /// <summary>Fill color. Unlike image/text, a color has no intrinsic aspect ratio — a
    /// color slot's AspectRatio is never set, so resizing is always free-form. Settable so the
    /// properties panel can re-pick a color.</summary>
    [ObservableProperty]
    private System.Windows.Media.Color? _overlayColor;
}

/// <summary>Unlike the other kinds, a video overlay needs an ongoing decode/loop session in the
/// core (like a live capture) rather than being registered once — see SourceSlot.IsStaticOverlay.</summary>
public partial class VideoOverlayContent : ObservableObject, IChromaKeyable
{
    public OverlayKind Kind => OverlayKind.Video;

    /// <summary>Source file path. Unlike the other overlay kinds this isn't rendered to pixels
    /// here — the core decodes and loops the file itself, exactly like a live capture session.
    /// Settable so the properties panel can re-browse; GoLiveViewModel reacts by starting a new
    /// decode session.</summary>
    [ObservableProperty]
    private string? _videoPath;

    [ObservableProperty]
    private bool _chromaKeyEnabled;

    [ObservableProperty]
    private System.Windows.Media.Color _chromaKeyColor = System.Windows.Media.Color.FromRgb(0x00, 0xB1, 0x40);

    [ObservableProperty]
    private double _chromaKeySimilarity = 40;
}

/// <summary>Renders recent chat messages for whichever stream service is currently selected —
/// re-rendered to pixels and re-registered (like image/text) each time new messages arrive,
/// rather than needing an ongoing core-side session.</summary>
public partial class ChatOverlayContent : ObservableObject, IOverlayContent
{
    public OverlayKind Kind => OverlayKind.Chat;

    /// <summary>Trimmed to a fixed backlog by GoLiveViewModel as new messages arrive — this is
    /// what both the local editor preview (bound directly) and the re-rendered pixels sent to
    /// the core are built from.</summary>
    public ObservableCollection<ChatMessage> ChatMessages { get; } = [];
}

/// <summary>A full-frame effect layer, not pixel content: everything below it in z-order gets
/// Gaussian-blurred by the core's compositor, everything above stays sharp. Has no meaningful
/// position/size, so it never appears as a box on the placement canvas — only in the overlay
/// list, where its z-position is what matters.</summary>
public partial class BlurOverlayContent : ObservableObject, IOverlayContent
{
    public OverlayKind Kind => OverlayKind.Blur;

    /// <summary>Gaussian blur radius (output-resolution pixels). Settable via the properties
    /// panel's strength slider — changes push a fresh Config so the effect updates live.</summary>
    [ObservableProperty]
    private double _blurRadius = 24;
}

/// <summary>A live-updating digital clock (count up from zero, or count down to zero from a
/// configured duration) — registered once and re-registered every second while running, same
/// mechanism as Chat's message-driven re-renders, just driven by a timer instead of incoming
/// messages.</summary>
public partial class TimerOverlayContent : ObservableObject, IOverlayContent
{
    public OverlayKind Kind => OverlayKind.Timer;

    [ObservableProperty]
    private TimerMode _timerMode = TimerMode.CountDown;

    /// <summary>Countdown target in seconds; meaningless for CountUp mode. Settable via the
    /// properties panel — a duration edit while running takes effect on the next tick.</summary>
    [ObservableProperty]
    private int _timerDurationSeconds = 300;

    /// <summary>Whether this timer is actively ticking — advanced once a second by
    /// GoLiveViewModel's own tick driver (see GoLiveViewModel.Timer.cs), toggled by the
    /// Start/Pause/Reset commands in SceneEditorViewModel. Deliberately not persisted: a loaded
    /// scene always restores paused, avoiding resume-after-restart drift.</summary>
    [ObservableProperty]
    private bool _isTimerRunning;

    /// <summary>Accumulated elapsed seconds from prior run segments (i.e. as of the last
    /// Pause) — combined with TimerStartedAtUtc while running to compute the live elapsed time
    /// without drifting across repeated Start/Pause cycles. Not persisted, same reasoning as
    /// IsTimerRunning.</summary>
    public double TimerElapsedBaseSeconds { get; set; }

    /// <summary>When this run segment started, or null while paused/stopped. Not persisted,
    /// same reasoning as IsTimerRunning.</summary>
    public DateTime? TimerStartedAtUtc { get; set; }

    /// <summary>Locally-rendered display string (e.g. "04:59"), kept in sync by
    /// SceneEditorViewModel.FormatTimerDisplay every time it's recomputed — lets the placement
    /// canvas show a live-ticking WYSIWYG preview the same way a Text overlay renders its
    /// OverlayText directly, without a round trip through the core's rasterized pixels.</summary>
    [ObservableProperty]
    private string _timerDisplayText = "";
}
