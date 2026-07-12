using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using StreamFlow.App.ViewModels.Pages;
using StreamFlow.Core.Helpers;

namespace StreamFlow.App.Services;

public sealed class SlotSettings
{
    public bool IsPrimary { get; set; }
    public bool IsOverlay { get; set; }
    public OverlayKind? OverlayKind { get; set; }
    public string? SourceId { get; set; }
    /// <summary>User-facing name shown in the Layers list, renamable via double-click there. Null
    /// for settings files predating this field — SceneEditorViewModel.BuildSceneFromSettings
    /// falls back to the old content-derived default (file name/text/etc.) in that case, so
    /// loading an old scene doesn't need a migration step.</summary>
    public string? DisplayName { get; set; }
    public string? ImagePath { get; set; }
    public string? OverlayText { get; set; }
    /// <summary>Stored as "#AARRGGBB" (matches WPF's Color.ToString() format) rather than
    /// serializing System.Windows.Media.Color's own shape, which is a messier JSON object.</summary>
    public string? OverlayColorHex { get; set; }
    public string? VideoPath { get; set; }
    /// <summary>See VideoOverlayContent.LoopVideo. Defaults to true so settings files predating
    /// this field keep the old always-loop behavior without needing a migration.</summary>
    public bool LoopVideo { get; set; } = true;
    public double XPercent { get; set; }
    public double YPercent { get; set; }
    public double WPercent { get; set; }
    public double HPercent { get; set; }
    public double CornerRadiusPercent { get; set; }
    public double BlurRadius { get; set; }
    public bool ChromaKeyEnabled { get; set; }
    /// <summary>Stored as "#AARRGGBB", same convention as <see cref="OverlayColorHex"/>.</summary>
    public string? ChromaKeyColorHex { get; set; }
    public double ChromaKeySimilarity { get; set; } = 40;
    public double OpacityPercent { get; set; } = 100;
    public int RotationDegrees { get; set; }
    /// <summary>Only meaningful for a Timer overlay slot — see SourceSlot.TimerMode/
    /// TimerDurationSeconds. Deliberately no running-state fields: a loaded timer always
    /// restores paused, avoiding resume-after-restart drift.</summary>
    public TimerMode TimerMode { get; set; } = TimerMode.CountDown;
    public int TimerDurationSeconds { get; set; } = 300;
    /// <summary>See TimerOverlayContent.AutoStartOnGoLive — unlike the running-state fields
    /// above, this is a configuration choice, not transient session state, so it does persist.</summary>
    public bool TimerAutoStartOnGoLive { get; set; }

    /// <summary>Shared text formatting (see TextStyle/IHasTextStyle) for any of Text/Chat/Timer —
    /// these three fields predate TextStyle and were Text-overlay-only, kept under their original
    /// names for backward compatibility with already-saved scene files. Nullable so settings
    /// files predating these fields fall back to TextStyle's own defaults (48pt, white, bold)
    /// rather than needing a migration.</summary>
    public double? TextFontSize { get; set; }
    /// <summary>Stored as "#AARRGGBB", same convention as <see cref="OverlayColorHex"/>.</summary>
    public string? TextFontColorHex { get; set; }
    public bool? TextIsBold { get; set; }

    /// <summary>Added alongside the rest of TextStyle — nullable/omittable the same way as the
    /// three fields above, for the same reason.</summary>
    public string? TextFontFamily { get; set; }
    public bool? TextIsItalic { get; set; }
    /// <summary>Stored as the TextHorizontalAlignment enum's own name ("Left"/"Center"/"Right").</summary>
    public string? TextAlignment { get; set; }
    public bool? TextOutlineEnabled { get; set; }
    /// <summary>Stored as "#AARRGGBB", same convention as <see cref="OverlayColorHex"/>.</summary>
    public string? TextOutlineColorHex { get; set; }
    public double? TextOutlineThickness { get; set; }
}

public sealed class SceneSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Scene";
    public List<SlotSettings> Slots { get; set; } = [];

    /// <summary>Real output resolution (actual pixels) — see GoLiveSceneViewModel's own doc
    /// comment. Null if never set (no primary has reported a resolution yet and none was
    /// manually/pre-selected).</summary>
    public uint? CanvasResolutionWidth { get; set; }
    public uint? CanvasResolutionHeight { get; set; }

    /// <summary>See GoLiveSceneViewModel.SwitchHotkey. Stored as the Key/ModifierKeys enums' own
    /// names (via ToString()/Enum.Parse) rather than Hotkey's own ToString(), which is a display
    /// format ("Ctrl + F1") not meant to round-trip. Both null means unassigned.</summary>
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

    /// <summary>How switching scenes animates in the composited/streamed output — session-wide,
    /// not per-scene. Stored as the plain wire string ("cut", "fade", "slide_left", etc.) rather
    /// than SceneTransitionKind directly, so this settings file has no dependency on that
    /// ViewModel-layer enum. See SceneEditorViewModel.TransitionKind/TransitionDurationMs.</summary>
    public string TransitionKind { get; set; } = "cut";
    public uint TransitionDurationMs { get; set; } = 400;

    /// <summary>Which audio device ids to capture and mix into the stream — global, not tied
    /// to any scene or profile (audio setup doesn't usually change scene-to-scene). Null (not
    /// just empty — the user may deliberately want nothing selected) until the user has ever
    /// touched the Audio Sources picker, in which case GoLiveViewModel falls back to whatever's
    /// currently marked default.</summary>
    public List<string>? SelectedAudioDeviceIds { get; set; }

    /// <summary>User-assigned display names for audio devices in the channel strip, keyed by
    /// device id — only present for devices the user has actually renamed away from their
    /// default OS name. Resettable per-device back to that default (see AudioSourceItem.
    /// ResetDisplayName), which just removes the entry here rather than storing the default
    /// name explicitly.</summary>
    public Dictionary<string, string> AudioDeviceDisplayNames { get; set; } = [];

    /// <summary>Overall stream-mix gain (0-100), applied on top of each individual channel
    /// strip's own VolumePercent rather than replacing it — see GoLiveViewModel.Audio.cs's
    /// EffectiveGain. Always present regardless of which/how many devices are selected, unlike
    /// the per-device channel strips.</summary>
    public double MasterVolumePercent { get; set; } = 100;

    /// <summary>Whether to publish the composited output as a named Spout2 source on launch (see
    /// native/crates/core/src/spout.rs and the Spout2 Integration Plan in the Obsidian vault).
    /// Independent of streaming/preview — purely an external-output feature for other Spout-aware
    /// apps (OBS + obs-spout2-plugin, TouchDesigner, Resolume, SpoutReceiver) on the same machine.</summary>
    public bool IsSpoutOutputEnabled { get; set; }

    /// <summary>Sender name other apps see when picking StreamFlow as a Spout source.</summary>
    public string SpoutSenderName { get; set; } = "StreamFlow";

    /// <summary>Whether to also write a local MP4 (stream-copy, same encoded packets sent to
    /// RTMP) alongside the live stream — see StartStreamCommand.RecordPath. Only ever active
    /// while actually streaming; the native encoder currently requires a real RTMP target to
    /// start at all (rtmp_url isn't optional), so there's no standalone "record without
    /// streaming" mode yet.</summary>
    public bool IsRecordingEnabled { get; set; }

    /// <summary>Folder the recording's auto-timestamped filename gets written into — a folder,
    /// not a fixed file path, so repeated recordings across sessions don't collide/overwrite.
    /// Null until the user has picked one.</summary>
    public string? RecordFolderPath { get; set; }

    /// <summary>Whether the Stream Deck HTTP/WebSocket server (see StreamDeckServerService)
    /// starts automatically on launch.</summary>
    public bool IsStreamDeckServerEnabled { get; set; }

    public int StreamDeckServerPort { get; set; } = 8080;

    /// <summary>Bearer token the Stream Deck plugin must present — generated once
    /// (StreamDeckServerService) and persisted here rather than regenerated per launch, so the
    /// user only has to paste it into the plugin's config once.</summary>
    public string? StreamDeckApiKey { get; set; }

    // Legacy fields for backwards compatibility
    public string? SceneName { get; set; }
    public List<SlotSettings>? Slots { get; set; }
    public StreamServiceKind SelectedService { get; set; } = StreamServiceKind.Twitch;
    public Dictionary<StreamServiceKind, string> ManualServerUrls { get; set; } = [];
    public uint BitrateKbps { get; set; } = 6000;
    public uint Fps { get; set; } = 30;
    public string? Encoder { get; set; }
}

/// <summary>
/// Persists Go Live tab state (selected service, RTMP target, encode settings, and
/// source/PiP/overlay layout) across app restarts. Deliberately separate from the audio
/// soundboard's `StreamFlow_Settings.json` (a different domain/model entirely).
/// </summary>
public sealed class GoLiveSettingsService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string SettingsFilePath => Path.Combine(AppDataPaths.RootFolder, "golive_settings.json");

    private static string StreamKeysFilePath => Path.Combine(AppDataPaths.RootFolder, "stream_keys.dat");

    public GoLiveSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath)) return new();
            var json = File.ReadAllText(SettingsFilePath);
            var settings = JsonSerializer.Deserialize<GoLiveSettings>(json, JsonOpts) ?? new();
            MigrateLegacyScene(settings);
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    private static void MigrateLegacyScene(GoLiveSettings settings)
    {
        if (settings.Scenes.Count > 0 || settings.Slots is not { Count: > 0 } legacySlots) return;

        var scene = new SceneSettings { Name = settings.SceneName ?? "Scene 1", Slots = legacySlots };
        settings.Scenes.Add(scene);
        settings.DefaultSceneId = scene.Id;
    }

    public void Save(GoLiveSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsFilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(settings, JsonOpts));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// A stream key is effectively a password for broadcasting to that channel, so — unlike
    /// the rest of Go Live's settings — it's DPAPI-protected in its own file instead of sitting
    /// in plain JSON, matching how the Twitch/YouTube OAuth tokens are already stored.
    /// </summary>
    public Dictionary<string, string> LoadStreamKeys()
    {
        try
        {
            if (!File.Exists(StreamKeysFilePath)) return [];
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(StreamKeysFilePath), null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(bytes)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or CryptographicException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void SaveStreamKeys(Dictionary<string, string> keys)
    {
        try
        {
            var dir = Path.GetDirectoryName(StreamKeysFilePath)!;
            Directory.CreateDirectory(dir);
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(keys));
            File.WriteAllBytes(StreamKeysFilePath, ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
