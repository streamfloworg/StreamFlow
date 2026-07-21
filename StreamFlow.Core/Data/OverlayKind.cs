namespace StreamFlow.Core.Data;

public enum OverlayKind
{
    Image,
    Text,
    Color,
    /// <summary>Unlike the other kinds, a video overlay needs an ongoing decode/loop session
    /// in the core (like a live capture) rather than being registered once.</summary>
    Video,
    /// <summary>Renders recent chat messages for whichever stream service is currently
    /// selected — re-rendered to pixels and re-registered (like image/text) each time new
    /// messages arrive, rather than needing an ongoing core-side session.</summary>
    Chat,
    /// <summary>A full-frame effect layer, not pixel content: everything below it in z-order
    /// gets Gaussian-blurred by the core's compositor, everything above stays sharp.</summary>
    Blur,
    /// <summary>A live-updating digital clock (count up from zero, or count down to zero from a
    /// configured duration) — registered once and re-registered every second while running.</summary>
    Timer,
    Group,
    Alert,
}

public enum StreamAlertType
{
    TwitchFollower,
    TwitchSubscriber,
    TwitchBits,
    TwitchRaid,
    YouTubeSubscriber,
    YouTubeMember,
    YouTubeSuperChat,
    GeneralDonation,
}

public enum AlertEntranceAnimation
{
    Fade,
    SlideLeft,
    SlideRight,
    SlideUp,
    SlideDown,
    Zoom,
}

public enum AlertExitAnimation
{
    Fade,
    SlideLeft,
    SlideRight,
    SlideUp,
    SlideDown,
    Zoom,
}

