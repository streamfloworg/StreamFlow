using StreamFlow.App.Services.AI;
using StreamFlow.Core.Data;

namespace StreamFlow.App.ViewModels.Pages;

/// <summary>Settings for CategoryMediaService — a toggle/folder-path pair mirroring
/// GoLiveViewModel.Recording.cs's RecordFolderPath exactly, plus which connected AI provider to
/// use. The actual check-folder/prompt/generate flow lives in CategoryMediaService itself
/// (triggered from GoLiveViewModel.Chat.cs's UpdateStreamInfoAsync); this partial only owns the
/// bound settings and the provider picker.</summary>
public partial class GoLiveViewModel
{
    [ObservableProperty]
    private bool _generateCategoryMediaEnabled;

    /// <summary>A folder StreamFlow scans for existing per-category media and writes newly
    /// generated images into — see ApplySettings for the %LOCALAPPDATA%\StreamFlow\CategoryMedia
    /// default applied when nothing's been picked yet.</summary>
    [ObservableProperty]
    private string? _categoryMediaFolderPath;

    [ObservableProperty]
    private bool _autoUseExistingCategoryMedia;

    public bool IsCategoryMediaFolderVisible => GenerateCategoryMediaEnabled;

    partial void OnGenerateCategoryMediaEnabledChanged(bool value)
    {
        ScheduleSaveSettings();
        OnPropertyChanged(nameof(IsCategoryMediaFolderVisible));
    }

    partial void OnCategoryMediaFolderPathChanged(string? value) => ScheduleSaveSettings();

    partial void OnAutoUseExistingCategoryMediaChanged(bool value) => ScheduleSaveSettings();

    [RelayCommand]
    private void BrowseCategoryMediaFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog();
        if (!string.IsNullOrEmpty(CategoryMediaFolderPath) && System.IO.Directory.Exists(CategoryMediaFolderPath))
            dialog.SelectedPath = CategoryMediaFolderPath;

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            CategoryMediaFolderPath = dialog.SelectedPath;
    }

    /// <summary>Wraps AiSettings.DefaultImageProviderId (owned by the AI provider settings from
    /// the prior phase, not duplicated here) — this feature is what finally gives that field a UI.</summary>
    public AiProviderProfile? SelectedCategoryMediaProvider
    {
        get => _aiProviders.FindProfile(AppModel.Instance.AiSettings.DefaultImageProviderId ?? "");
        set
        {
            AppModel.Instance.AiSettings.DefaultImageProviderId = value?.Id;
            ScheduleSaveSettings();
            OnPropertyChanged();
        }
    }

    // Filters on IsEnabled, not IsConnected — ConnectionStatus deliberately resets to Unknown
    // every launch (see AiProviderRegistryService's doc comment) and only becomes Connected via
    // an explicit Test Connection click in Settings *this session*. GetImageClient below doesn't
    // require Connected either — it only needs SupportsImage and a saved API key — so gating this
    // picker on IsConnected would leave it empty every session even for fully working providers.
    public IEnumerable<AiProviderProfile> AvailableCategoryMediaProviders =>
        _aiProviders.Profiles.Where(p => p.SupportsImage && p.IsEnabled);
}
