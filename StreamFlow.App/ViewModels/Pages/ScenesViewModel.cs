using StreamFlow.App.Services;

namespace StreamFlow.App.ViewModels.Pages;

/// <summary>Standalone Scenes page: scene/layer positioning and setup without Go Live's
/// streaming/profile/audio/chat concerns and without Go Live's own "Save Layout" persistence
/// action — see SceneEditorViewModel's own doc comment for what's actually shared with Go Live.
///
/// Deliberately starts with nothing shown: even though SceneEditor's Scenes/ActiveScene are
/// already populated by the time this page is first opened (Go Live loads them eagerly at app
/// startup, regardless of navigation), this page still requires an explicit Load action before
/// displaying/editing anything — so a user never accidentally edits whatever happens to be live
/// without meaning to. <see cref="IsSceneSetLoaded"/> is purely a local view gate: closing it
/// (via CloseSceneSetCommand) never touches the shared Scenes/ActiveScene data itself.</summary>
public partial class ScenesViewModel : ViewModel
{
    private readonly IDialogService _dialogs;

    public SceneEditorViewModel SceneEditor { get; }

    [ObservableProperty]
    private bool _isSceneSetLoaded;

    public ScenesViewModel(SceneEditorViewModel sceneEditor, IDialogService dialogs)
    {
        SceneEditor = sceneEditor;
        _dialogs = dialogs;
    }

    /// <summary>Reveals the editor for whatever Go Live currently has active — no actual load
    /// happens here, since SceneEditor's Scenes/ActiveScene already reflect it (same shared
    /// state), this just stops hiding it behind the blank prompt.</summary>
    [RelayCommand]
    private void LoadActiveSceneSet() => IsSceneSetLoaded = true;

    /// <summary>Starts a brand-new blank layout rather than loading an existing one — replaces
    /// Scenes/ActiveScene's content (via SceneEditor), which Go Live will also see immediately
    /// since it's the same shared state.</summary>
    [RelayCommand]
    private void CreateNewSceneSet()
    {
        SceneEditor.CreateNewSceneSet();
        IsSceneSetLoaded = true;
    }

    /// <summary>Imports a .sfset file from disk and immediately loads it for editing — unlike
    /// LoadActiveSceneSet, this replaces Scenes/ActiveScene's content (via SceneEditor), which Go
    /// Live will also see immediately since it's the same shared state.</summary>
    [RelayCommand]
    private async Task LoadSceneSetFromDiskAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Load Scene Set",
            Filter = "Scene Set Files|*.sfset|All Files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var reg = SceneEditor.ImportSceneSetFile(dialog.FileName);
            await SceneEditor.LoadSceneSetForEditingAsync(reg);
            IsSceneSetLoaded = true;
        }
        catch (Exception ex)
        {
            await _dialogs.WarningAsync("Load Scene Set", $"Failed to load Scene Set: {ex.Message}");
        }
    }

    /// <summary>Writes the current Scenes content to a portable .sfset file the user picks —
    /// distinct from the in-place "Save" (which just overwrites whatever's already loaded).</summary>
    [RelayCommand]
    private async Task SaveSceneSetToDiskAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save Scene Set",
            Filter = "Scene Set Files|*.sfset",
            FileName = $"{SceneEditor.ActiveSceneSet?.Name ?? "Scene Set"}.sfset"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            SceneEditor.ExportActiveSceneSet(dialog.FileName);
        }
        catch (Exception ex)
        {
            await _dialogs.WarningAsync("Save Scene Set", $"Failed to save Scene Set: {ex.Message}");
        }
    }

    // ── Library browser (blank-state list of already-imported Scene Sets) ──────────

    /// <summary>Loads an already-registered Scene Set straight from the browser list, without
    /// re-picking its .sfset file from disk — unlike LoadSceneSetFromDiskAsync, which both
    /// imports and loads in one step, this one's already imported.</summary>
    [RelayCommand]
    private async Task LoadRegisteredSceneSetAsync(SceneSetRegistration reg)
    {
        await SceneEditor.LoadSceneSetForEditingAsync(reg);
        IsSceneSetLoaded = true;
    }

    /// <summary>Exports a registered Scene Set directly from the browser list — unlike
    /// SaveSceneSetToDiskAsync (which exports whatever's currently loaded into this editor),
    /// this reads the picked entry's own cached files, so it works without loading it first.</summary>
    [RelayCommand]
    private async Task ExportRegisteredSceneSetAsync(SceneSetRegistration reg)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Scene Set",
            Filter = "Scene Set Files|*.sfset",
            FileName = $"{reg.Name}.sfset"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            SceneEditor.ExportRegisteredSceneSet(reg, dialog.FileName);
        }
        catch (Exception ex)
        {
            await _dialogs.WarningAsync("Export Scene Set", $"Failed to export Scene Set: {ex.Message}");
        }
    }

    /// <summary>Deletes a registered Scene Set from the browser list. Unlike Go Live's own
    /// Manage-Scene-Sets removal, this has no "linked to the active streaming profile" guard —
    /// this page has no concept of streaming profiles — so Go Live's own removal path is still
    /// what actually protects a profile-linked set; this is a narrower, simpler action.</summary>
    [RelayCommand]
    private async Task DeleteRegisteredSceneSetAsync(SceneSetRegistration reg)
    {
        var confirmed = await _dialogs.ConfirmAsync(
            "Delete Scene Set",
            $"Delete '{reg.Name}'? This removes its cached files and cannot be undone.",
            "Delete", "Cancel");
        if (!confirmed) return;

        SceneEditor.UninstallSceneSet(reg);
    }

    /// <summary>Closes this page's own editor view — purely local, never touches the shared
    /// Scenes/ActiveScene data, so Go Live is completely unaffected.</summary>
    [RelayCommand]
    private void CloseSceneSet() => IsSceneSetLoaded = false;
}
