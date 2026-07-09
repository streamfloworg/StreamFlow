using System.Text.Json.Serialization;

namespace StreamFlow.App.Services.Core;

/// <summary>Lifecycle state of the streamflow-core child process, as observed by <see cref="CoreBridgeService"/>.</summary>
public enum CoreState
{
    NotStarted,
    Running,
    Exited,
    BinaryMissing,
}

// ── Commands (C# → Rust core, newline-delimited JSON on stdin) ────────────
//
// Wire format matches native/crates/ipc/src/lib.rs exactly: tag property
// "type", snake_case discriminant values, snake_case field names.

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AuthCommand), "auth")]
[JsonDerivedType(typeof(HelloCommand), "hello")]
[JsonDerivedType(typeof(PingCommand), "ping")]
[JsonDerivedType(typeof(ShutdownCommand), "shutdown")]
[JsonDerivedType(typeof(ExitCommand), "exit")]
[JsonDerivedType(typeof(StandbyCommand), "standby")]
[JsonDerivedType(typeof(GetSourcesCommand), "get_sources")]
[JsonDerivedType(typeof(GetAudioDevicesCommand), "get_audio_devices")]
[JsonDerivedType(typeof(StartCaptureCommand), "start_capture")]
[JsonDerivedType(typeof(StopCaptureCommand), "stop_capture")]
[JsonDerivedType(typeof(StartStreamCommand), "start_stream")]
[JsonDerivedType(typeof(StopStreamCommand), "stop_stream")]
[JsonDerivedType(typeof(StartAudioMonitorCommand), "start_audio_monitor")]
[JsonDerivedType(typeof(StopAudioMonitorCommand), "stop_audio_monitor")]
[JsonDerivedType(typeof(SetDeviceVolumeCommand), "set_device_volume")]
[JsonDerivedType(typeof(SetDeviceMuteCommand), "set_device_mute")]
[JsonDerivedType(typeof(SetAudioMixCommand), "set_audio_mix")]
[JsonDerivedType(typeof(EnablePreviewCommand), "enable_preview")]
[JsonDerivedType(typeof(DisablePreviewCommand), "disable_preview")]
[JsonDerivedType(typeof(ConfigCommand), "config")]
[JsonDerivedType(typeof(AddStaticOverlayCommand), "add_static_overlay")]
public abstract record CoreCommand;

/// <summary>First message on stdin: establishes session credentials.</summary>
public sealed record AuthCommand(string Token, string PipeId) : CoreCommand;

/// <summary>First message on the data pipe after connecting: proves identity.</summary>
public sealed record HelloCommand(string Token) : CoreCommand;

/// <summary>Liveness probe — core must respond with <see cref="PongEvent"/>.</summary>
public sealed record PingCommand : CoreCommand;

public sealed record ShutdownCommand : CoreCommand;
public sealed record ExitCommand : CoreCommand;

/// <summary>Keep-alive: core exits after 30s without one. Send well within that window.</summary>
public sealed record StandbyCommand : CoreCommand;
public sealed record GetSourcesCommand : CoreCommand;
public sealed record GetAudioDevicesCommand : CoreCommand;
public sealed record StartCaptureCommand(string SourceId, string? OverlayHwnd = null) : CoreCommand;
public sealed record StopCaptureCommand(string SourceId) : CoreCommand;

/// <summary>Begin encoding the active capture layout and pushing to RTMP.</summary>
public sealed record StartStreamCommand(
    string RtmpUrl,
    uint BitrateKbps,
    uint Fps,
    uint? OutputWidth,
    uint? OutputHeight,
    string? FitMode,
    string Encoder,
    StreamSourceDef[] Sources,
    AudioSourceConfig[] AudioSources,
    string? RecordPath) : CoreCommand;

public sealed record StopStreamCommand : CoreCommand;

/// <summary>Begin capturing and metering a device independent of streaming, for the Audio
/// Sources panel's live level meters while just setting up. Core responds with a stream of
/// <see cref="AudioDeviceLevelEvent"/> for this device id roughly every 100ms until
/// <see cref="StopAudioMonitorCommand"/> for the same id.</summary>
public sealed record StartAudioMonitorCommand(string DeviceId) : CoreCommand;
public sealed record StopAudioMonitorCommand(string DeviceId) : CoreCommand;

/// <summary>Sets the device's actual OS-level (Volume Mixer) master volume — distinct from the
/// per-source mix gain in <see cref="AudioSourceConfig"/>. Fire-and-forget: no response, since
/// <see cref="AudioDeviceVolumeEvent"/> (emitted periodically for any monitored device) already
/// reflects the result.</summary>
public sealed record SetDeviceVolumeCommand(string DeviceId, float Volume) : CoreCommand;

/// <summary>Sets the device's actual OS-level mute state. Fire-and-forget, see <see cref="SetDeviceVolumeCommand"/>.</summary>
public sealed record SetDeviceMuteCommand(string DeviceId, bool Muted) : CoreCommand;

/// <summary>Live-updates one audio source's stream-mix gain/mute/solo (see
/// <see cref="AudioSourceConfig"/>) while a stream is already running — a no-op core-side if no
/// stream is active. Distinct from <see cref="SetDeviceVolumeCommand"/>/<see cref="SetDeviceMuteCommand"/>,
/// which control the OS-level device volume, not this app's own mix. Fire-and-forget: no response.</summary>
public sealed record SetAudioMixCommand(string DeviceId, float Gain, bool Muted, bool Solo) : CoreCommand;

/// <summary>Allow the data pipe to deliver preview frames. Requires an active capture session.</summary>
public sealed record EnablePreviewCommand : CoreCommand;
public sealed record DisablePreviewCommand : CoreCommand;

/// <summary>
/// Tells the compositor how every source (primary included — it's just another positioned
/// layer, not a privileged base) is placed and z-ordered. Required before the compositor will
/// emit any composited frame — both preview and StartStream read from its output, so this must
/// be sent before either will show anything. <see cref="CanvasWidth"/>/<see cref="CanvasHeight"/>
/// are the canvas resolution to render at when no source is flagged primary yet (or it hasn't
/// reported its real resolution) — a live primary frame's own dimensions always take priority
/// once available, so these are only meaningful for primary-less scenes.
/// </summary>
public sealed record ConfigCommand(StreamSourceDef[] Sources, uint? CanvasWidth = null, uint? CanvasHeight = null, TransitionDef? Transition = null) : CoreCommand;

/// <summary>Animates from whatever was last composited to this Config's <see cref="ConfigCommand.Sources"/>
/// layout instead of swapping instantly. Only set on an actual scene switch (see
/// SceneEditorViewModel.ActivateSceneAsync) — every other Config send omits this and keeps the
/// plain instant-cut behavior. <see cref="Kind"/> is a plain lowercase string ("cut", "fade",
/// "slide_left", "slide_right", "slide_up", "slide_down") rather than a serialized enum — matches
/// the Rust side's `#[serde(rename_all = "snake_case")]` `TransitionKind` without relying on a
/// JsonStringEnumConverter naming policy lining up exactly on both sides.</summary>
public sealed record TransitionDef(string Kind, uint DurationMs);

/// <summary>Placement of one source within the composited output frame, as percentages.
/// CornerRadiusPercent is a percentage of half this source's shorter placed side (0 = square,
/// 100 = a full pill/circle) — applies uniformly, including to the primary.</summary>
/// <summary>BlurRadius nonzero marks a blur layer: no pixel content of its own — the core
/// blurs the entire frame composited so far (everything earlier in the sources list) by that
/// many output-resolution pixels, leaving later sources sharp on top. Placement fields are
/// ignored for such defs; their SourceId starts with "blur:".</summary>
public sealed record StreamSourceDef(
    string SourceId, bool IsPrimary, float XPercent, float YPercent, float WPercent, float HPercent,
    float CornerRadiusPercent = 0, uint BlurRadius = 0, ChromaKeyDef? ChromaKey = null, float Opacity = 1.0f,
    ushort RotationDegrees = 0);

/// <summary>Color-key transparency (e.g. green-screen removal) for one source — Similarity is
/// 0.0-1.0, the normalized color-distance threshold below which a pixel is treated as key color
/// (fully transparent), with a fixed softness band above that for an anti-aliased edge rather
/// than a hard cutout. See compositor.rs's chroma_mask for the exact math.</summary>
public sealed record ChromaKeyDef(byte R, byte G, byte B, float Similarity);

/// <summary>One audio device's mix settings for <see cref="StartStreamCommand"/>. Solo/mute are
/// resolved core-side at mix time: if any source has Solo set, only soloed sources are audible
/// (muted or not); otherwise every non-muted source mixes in at its own Gain.</summary>
public sealed record AudioSourceConfig(string DeviceId, float Gain = 1.0f, bool Muted = false, bool Solo = false);

/// <summary>
/// Registers a static overlay's already-rendered pixels under <paramref name="SourceId"/> so it
/// can be placed and composited exactly like a PiP capture source (include the same SourceId in
/// a subsequent <see cref="ConfigCommand"/>). Covers every static overlay kind (image, text,
/// solid color, ...) — rendering/decoding happens here in C# (which already has rich
/// text/imaging APIs), so the core stays generic instead of knowing about content types.
/// Unlike <see cref="StartCaptureCommand"/>, there's no ongoing session and thus nothing to
/// stop — omit it from the next Config to stop compositing it.
/// Core responds with <see cref="CaptureStartedEvent"/> or <see cref="ErrorEvent"/>.
/// </summary>
public sealed record AddStaticOverlayCommand(string SourceId, uint Width, uint Height, string PixelsBase64) : CoreCommand;

// ── Events (Rust core → C#, newline-delimited JSON on stdout) ────────────

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ReadyEvent), "ready")]
[JsonDerivedType(typeof(PongEvent), "pong")]
[JsonDerivedType(typeof(ErrorEvent), "error")]
[JsonDerivedType(typeof(SourcesEvent), "sources")]
[JsonDerivedType(typeof(AudioDevicesEvent), "audio_devices")]
[JsonDerivedType(typeof(CaptureStartedEvent), "capture_started")]
[JsonDerivedType(typeof(CaptureStoppedEvent), "capture_stopped")]
[JsonDerivedType(typeof(StreamStartedEvent), "stream_started")]
[JsonDerivedType(typeof(StreamStoppedEvent), "stream_stopped")]
[JsonDerivedType(typeof(StreamStatusEvent), "stream_status")]
[JsonDerivedType(typeof(AudioLevelEvent), "audio_level")]
[JsonDerivedType(typeof(AudioDeviceLevelEvent), "audio_device_level")]
[JsonDerivedType(typeof(AudioDeviceVolumeEvent), "audio_device_volume")]
[JsonDerivedType(typeof(CoreStatsEvent), "core_stats")]
public abstract record CoreEvent;

/// <summary>Emitted once on stdout after the data pipe is bound and ready.</summary>
public sealed record ReadyEvent(uint Version, uint Pid, string Pipe, string ShmName, uint ShmSize) : CoreEvent;

/// <summary>Response to <see cref="PingCommand"/>.</summary>
public sealed record PongEvent(uint Version) : CoreEvent;

/// <summary>A non-fatal error occurred inside the core.</summary>
public sealed record ErrorEvent(string Code, string Message) : CoreEvent;

public sealed record SourcesEvent(NativeCaptureSource[] Items) : CoreEvent;

/// <summary>Width/Height are only populated when the core knows the source's native resolution
/// synchronously at start time — currently just video overlays, which (unlike monitor/window/
/// webcam sources) aren't enumerable via <see cref="GetSourcesCommand"/> ahead of time since
/// they're picked as a file path directly.</summary>
public sealed record CaptureStartedEvent(string SourceId, uint? Width = null, uint? Height = null) : CoreEvent;
public sealed record CaptureStoppedEvent : CoreEvent;

/// <summary>Emitted when streaming starts successfully.</summary>
public sealed record StreamStartedEvent(uint Width, uint Height) : CoreEvent;
public sealed record StreamStoppedEvent : CoreEvent;

/// <summary>Periodic encoding statistics, emitted roughly once per second.</summary>
public sealed record StreamStatusEvent(ulong Frame, float Fps, uint BitrateKbps, ulong Dropped) : CoreEvent;

/// <summary>Periodic (every ~3s) resource snapshot for the status bar — deliberately coarse,
/// nothing here is time-sensitive. VramUsedMb/VramTotalMb are null when the VRAM query itself
/// isn't available (e.g. an unsupported driver), independent of whether any capture is active.</summary>
public sealed record CoreStatsEvent(float CpuPercent, float WorkingSetMb, float? VramUsedMb, float? VramTotalMb) : CoreEvent;

/// <summary>Width/Height are the source's native capture resolution (0 if not known — currently
/// only webcams, whose resolution isn't read without opening the device).</summary>
public sealed record NativeCaptureSource(string Id, string Name, string Kind, uint Width, uint Height);

/// <summary>Response to <see cref="GetAudioDevicesCommand"/>.</summary>
public sealed record AudioDevicesEvent(NativeAudioDevice[] Items) : CoreEvent;

/// <summary>Kind is "output" (system playback — desktop/game audio), "microphone", or
/// "capture" (a software/virtual device: VoiceMeeter, VB-CABLE, Stereo Mix, etc.).</summary>
public sealed record NativeAudioDevice(string Id, string Name, string Kind, bool IsDefault);

/// <summary>Emitted at ~100ms intervals while audio capture is active during streaming — one
/// aggregate peak for the whole mixed stream. PeakDb is dBFS; float.NegativeInfinity means the
/// capture is running but produced silence.</summary>
public sealed record AudioLevelEvent(float PeakDb) : CoreEvent;

/// <summary>Same shape as <see cref="AudioLevelEvent"/> but tagged per-device — emitted for any
/// device with an active monitor session (<see cref="StartAudioMonitorCommand"/>) or that's part
/// of the current stream's mix.</summary>
public sealed record AudioDeviceLevelEvent(string DeviceId, float PeakDb) : CoreEvent;

/// <summary>Periodic (much slower than <see cref="AudioDeviceLevelEvent"/>) OS-level volume/mute
/// state for a monitored device — the same volume Windows' own Volume Mixer shows/controls,
/// which can change externally (another app, physical volume keys, Windows Settings) at any
/// time. Volume is a linear 0.0-1.0 scalar, matching IAudioEndpointVolume's own convention.</summary>
public sealed record AudioDeviceVolumeEvent(string DeviceId, float Volume, bool Muted) : CoreEvent;

// ── Data pipe frame (binary, after handshake) ─────────────────────────────

/// <summary>
/// Wire format: 8-byte header [u32-LE frameType=1][u32-LE payloadLen], then payload
/// [u8 sourceIdLen][sourceId bytes][u32-LE width][u32-LE height][RGBA pixels].
/// </summary>
public sealed record VideoFrame(string SourceId, int Width, int Height, byte[] BgraPixels);
