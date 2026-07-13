using System.Windows.Input;

using StreamFlow.App.ViewModels.Pages;
using StreamFlow.Core.AudioProperties;
using StreamFlow.Core.Data;

namespace StreamFlow.App.Services;

/// <summary>Checks a candidate hotkey against every other hotkey currently assigned anywhere in
/// the app — soundboard clips, the "stop all audio" hotkey, the hardcoded media play/pause key,
/// and scene-switch hotkeys — so assigning a duplicate combo can be flagged instead of silently
/// producing ambiguous behavior (the existing dispatch loop in MainWindow.HotKeyHook_OnKeyboard
/// fires every match on a given keypress, so two things sharing one combo both fire at once).
/// A DI singleton purely because it needs SceneEditorViewModel; AppModel.Instance is reached
/// directly like everywhere else in this app rather than also being injected.</summary>
public sealed class HotkeyConflictService(SceneEditorViewModel sceneEditor)
{
    /// <summary>Returns a human-readable description of whatever the candidate collides with, or
    /// null if it's free to assign. <paramref name="excludingOwner"/> is the item currently being
    /// (re)assigned — by reference — so re-confirming an item's own existing combo doesn't flag
    /// itself as a conflict.</summary>
    public string? FindConflict(Hotkey candidate, object? excludingOwner = null)
    {
        foreach (var audio in AppModel.Instance.Audios)
        {
            if (!ReferenceEquals(audio, excludingOwner) && Matches(audio.Hotkey, candidate))
                return $"the soundboard clip '{audio.Name}'";
        }

        if (Matches(AppModel.Instance.Settings.StopAllAudioHotKey, candidate))
            return "\"Stop All Audio\"";

        if (Matches(new Hotkey(Key.MediaPlayPause, ModifierKeys.None), candidate))
            return "the system media play/pause key";

        foreach (var scene in sceneEditor.Scenes)
        {
            if (!ReferenceEquals(scene, excludingOwner) && Matches(scene.SwitchHotkey, candidate))
                return $"the scene '{scene.Name}'";
        }

        return null;
    }

    private static bool Matches(Hotkey? a, Hotkey b) =>
        a is not null && a.Key == b.Key && a.Modifiers == b.Modifiers;
}
