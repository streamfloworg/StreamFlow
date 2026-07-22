using System;
using System.Collections.Generic;

namespace StreamFlow.Core.Data;

public sealed class SlotSettings
{
    public bool IsPrimary { get; set; }
    public bool IsOverlay { get; set; }
    public OverlayKind? OverlayKind { get; set; }
    public string? SourceId { get; set; }
    public string? DisplayName { get; set; }
    public string? ImagePath { get; set; }
    public string? OverlayText { get; set; }
    public string? OverlayColorHex { get; set; }
    public string? VideoPath { get; set; }
    public bool LoopVideo { get; set; } = true;
    public double XPercent { get; set; }
    public double YPercent { get; set; }
    public double WPercent { get; set; }
    public double HPercent { get; set; }
    public double CornerRadiusPercent { get; set; }
    public double BlurRadius { get; set; }
    public bool ChromaKeyEnabled { get; set; }
    public string? ChromaKeyColorHex { get; set; }
    public double ChromaKeySimilarity { get; set; } = 40;
    public double OpacityPercent { get; set; } = 100;
    public int RotationDegrees { get; set; }
    public TimerMode TimerMode { get; set; } = TimerMode.CountDown;
    public int TimerDurationSeconds { get; set; } = 300;
    public bool TimerAutoStartOnGoLive { get; set; }

    public double? TextFontSize { get; set; }
    public string? TextFontColorHex { get; set; }
    public bool? TextIsBold { get; set; }

    public string? TextFontFamily { get; set; }
    public bool? TextIsItalic { get; set; }
    public string? TextAlignment { get; set; }
    public bool? TextOutlineEnabled { get; set; }
    public string? TextOutlineColorHex { get; set; }
    public double? TextOutlineThickness { get; set; }
    public List<string>? GroupChildIds { get; set; }
    public List<SlotSettings>? Children { get; set; }
    public bool LockChildren { get; set; } = true;

    public StreamAlertType AlertType { get; set; }
    public int AlertDurationSeconds { get; set; } = 5;
    public AlertEntranceAnimation AlertEntranceAnimation { get; set; } = AlertEntranceAnimation.Fade;
    public AlertExitAnimation AlertExitAnimation { get; set; } = AlertExitAnimation.Fade;
}

public sealed class SceneSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Scene";
    public List<SlotSettings> Slots { get; set; } = [];
    public uint? CanvasResolutionWidth { get; set; }
    public uint? CanvasResolutionHeight { get; set; }
    public string? SwitchHotkeyKey { get; set; }
    public string? SwitchHotkeyModifiers { get; set; }
}

public sealed class SceneSetRegistration
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Imported Scene Set";
    public string Author { get; set; } = "";
    public string ExtractPath { get; set; } = "";
}

public sealed class StreamingProfileSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Profile";
    public StreamServiceKind ServiceKind { get; set; } = StreamServiceKind.Twitch;
    public string ServerUrl { get; set; } = "";
    public uint BitrateKbps { get; set; } = 6000;
    public uint Fps { get; set; } = 30;
    public string Encoder { get; set; } = "libx264";
    public string? LinkedSceneSetId { get; set; }
    public Dictionary<string, List<SceneSettings>> SceneSetOverrides { get; set; } = [];
}

public sealed class GoLiveSettings
{
    public List<SceneSettings> Scenes { get; set; } = [];
    public string? DefaultSceneId { get; set; }

    public List<StreamingProfileSettings> Profiles { get; set; } = [];
    public string? ActiveProfileId { get; set; }
    public string? DefaultProfileId { get; set; }

    public List<SceneSetRegistration> RegisteredSceneSets { get; set; } = [];

    public string TransitionKind { get; set; } = "cut";
    public uint TransitionDurationMs { get; set; } = 400;

    public List<string>? SelectedAudioDeviceIds { get; set; }

    public Dictionary<string, string> AudioDeviceDisplayNames { get; set; } = [];

    public double MasterVolumePercent { get; set; } = 100;

    public bool IsSpoutOutputEnabled { get; set; }

    public string SpoutSenderName { get; set; } = "StreamFlow";

    public bool IsRecordingEnabled { get; set; }

    public string? RecordFolderPath { get; set; }

    public bool IsStreamDeckServerEnabled { get; set; }

    public int StreamDeckServerPort { get; set; } = 8080;

    public string? StreamDeckApiKey { get; set; }

    public bool IsDuckingEnabled { get; set; }
    public string? DuckingTriggerDeviceId { get; set; }
    public float DuckingThresholdDb { get; set; } = -30.0f;
    public float DuckingDepth { get; set; } = 0.8f;
    public float DuckingAttackMs { get; set; } = 10.0f;
    public float DuckingReleaseMs { get; set; } = 300.0f;
    public float DuckingHoldMs { get; set; } = 100.0f;
    public List<string> DuckingTargetDeviceIds { get; set; } = [];

    // Legacy fields for backwards compatibility
    public string? SceneName { get; set; }
    public List<SlotSettings>? Slots { get; set; }
    public StreamServiceKind SelectedService { get; set; } = StreamServiceKind.Twitch;
    public Dictionary<StreamServiceKind, string> ManualServerUrls { get; set; } = [];
    public uint BitrateKbps { get; set; } = 6000;
    public uint Fps { get; set; } = 30;
    public string? Encoder { get; set; }
}
