using System.Collections.ObjectModel;

using StreamFlow.Core.AudioProperties;

namespace StreamFlow.App.ViewModels.Pages;

/// <summary>
/// One named, switchable Go Live layout: its own source slots (capture sources + overlays,
/// freely ordered/positioned — a capture source flagged primary carries no positional privilege
/// of its own) and overlays. Only the active scene's slots have live capture sessions in the
/// core — see GoLiveViewModel's ActivateSceneAsync/DeactivateSceneAsync.
/// </summary>
public partial class GoLiveSceneViewModel : ObservableObject
{
    public string Id { get; }

    [ObservableProperty]
    private string _name;

    public ObservableCollection<SourceSlot> Slots { get; } = [];

    /// <summary>Real output resolution (actual pixels, e.g. 1920x1080) — distinct from
    /// SourceSlot.CanvasWidth/CanvasHeight (always a fixed 640-wide editor reference frame used
    /// purely for percent/pixel UI math). Set from whichever slot is flagged primary once its
    /// real resolution is known (see SceneEditorViewModel.ApplyAspectRatio), or manually/from a
    /// pre-selected device for a primary-less scene (see SceneEditorViewModel.SetCanvasResolution)
    /// — sent to the core as ConfigCommand's CanvasWidth/CanvasHeight, only actually used there
    /// when no live primary frame is available yet.</summary>
    [ObservableProperty]
    private uint? _canvasResolutionWidth;

    [ObservableProperty]
    private uint? _canvasResolutionHeight;

    /// <summary>The placement canvas's reference pixel size — always the same fixed 640-wide
    /// frame replicated onto every slot (SourceSlot.CanvasWidth/CanvasHeight) for their own
    /// percent/pixel drag/resize/snap math, but exposed here too so scene-level UI (the
    /// composited-preview element, alignment guides) doesn't need to index into any particular
    /// slot to read it — works even for an empty or overlay-only scene. Kept in sync with
    /// CanvasResolutionWidth/Height's aspect ratio by SceneEditorViewModel.UpdateCanvasReference.</summary>
    [ObservableProperty]
    private double _canvasWidth = SourceSlot.DefaultCanvasWidth;

    [ObservableProperty]
    private double _canvasHeight = SourceSlot.DefaultCanvasWidth * 9.0 / 16.0;

    /// <summary>Global (works regardless of window focus, same KeyboardHook-based dispatch as
    /// soundboard clip hotkeys — see MainWindow.HotKeyHook_OnKeyboard) shortcut that switches Go
    /// Live to this scene. Null means unassigned. See HotkeyConflictService for the app-wide
    /// duplicate-combo check run before this gets set.</summary>
    [ObservableProperty]
    private Hotkey? _switchHotkey;

    public GoLiveSceneViewModel(string id, string name)
    {
        Id = id;
        Name = name;
    }
}
