namespace StreamFlow.App.ViewModels.Pages;

/// <summary>Local MP4 recording — always alongside an active stream, not standalone (see
/// GoLiveSettingsService.IsRecordingEnabled's own doc comment for why: the native encoder
/// currently requires a real RTMP target to start at all). A plain UI-level toggle/folder-path
/// pair threaded into StartStreamCommand.RecordPath by StartStreamAsync.</summary>
public partial class GoLiveViewModel
{
    [ObservableProperty]
    private bool _isRecordingEnabled;

    /// <summary>A folder, not a fixed file path — BuildRecordPathIfEnabled appends a fresh
    /// timestamped filename per stream so repeated recordings never collide/overwrite each
    /// other.</summary>
    [ObservableProperty]
    private string? _recordFolderPath;

    [ObservableProperty]
    private bool _isRecordOnlyMode;

    public bool IsRecordingFolderVisible => IsRecordOnlyMode || IsRecordingEnabled;

    partial void OnIsRecordingEnabledChanged(bool value)
    {
        ScheduleSaveSettings();
        OnPropertyChanged(nameof(IsRecordingFolderVisible));
    }

    partial void OnRecordFolderPathChanged(string? value) => ScheduleSaveSettings();

    partial void OnIsRecordOnlyModeChanged(bool value)
    {
        ScheduleSaveSettings();
        RefreshStartStreamAvailability();
        OnPropertyChanged(nameof(IsRecordingFolderVisible));
    }

    [RelayCommand]
    private void BrowseRecordFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog();
        if (!string.IsNullOrEmpty(RecordFolderPath) && System.IO.Directory.Exists(RecordFolderPath))
            dialog.SelectedPath = RecordFolderPath;

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            RecordFolderPath = dialog.SelectedPath;
    }

    /// <summary>Null when recording isn't enabled or no folder has been picked — StartStreamAsync
    /// passes this straight through as StartStreamCommand.RecordPath.</summary>
    internal string? BuildRecordPathIfEnabled(bool force = false) =>
        (force || IsRecordingEnabled) && !string.IsNullOrEmpty(RecordFolderPath)
            ? System.IO.Path.Combine(RecordFolderPath, $"StreamFlow_{DateTime.Now:yyyyMMdd_HHmmss}.mp4")
            : null;
}
