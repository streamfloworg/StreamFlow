using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;

// iNKORE usings removed

using Microsoft.Extensions.DependencyInjection;

using StreamFlow.App.Services;
using StreamFlow.Core.AudioHandling;
using StreamFlow.Core.Cache;
using StreamFlow.Core.Data;

using Velopack;

namespace StreamFlow.App.ViewModels.Pages;
public partial class SettingsViewModel : ViewModel
{
    private readonly UpdateService _updates = App.Services.GetRequiredService<UpdateService>();

    [ObservableProperty]
    private bool _isInitialized = false;

    [ObservableProperty]
    private string _appVersion = string.Empty;

    /// <summary>"Up to date", "Update available: vX.Y.Z", "Checking for updates…", an error
    /// message, or empty before the first check this session. Only meaningful for the
    /// unpackaged/Velopack distribution — see UpdateService's own doc comment.</summary>
    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    private UpdateInfo? _pendingUpdate;

    public bool ShowUpdateCheck => _updates.IsInstalled;

    // CurrentTheme removed

    [ObservableProperty]
    private ApplicationSettings _settings = AppModel.Instance.Settings;

    public static ObservableCollection<AudioDevice> Outputs { get; set; } = [];

    public static ICollectionView? AudioOutputs { get; set; }

    public static ObservableCollection<AudioDevice> Inputs { get; set; } = [];

    public SettingsViewModel()
    {
        _ = RefreshDevicesAsync();
        AudioOutputs = CollectionViewSource.GetDefaultView(Outputs);
        AudioOutputs.Refresh();
    }

    public void OutputDeviceSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not null)
        {
            var selectedDevice = AudioEngine.Engine.PlaybackDevices.FirstOrDefault(i => i.Name == AppModel.Instance.Settings.OutputDevice);
            AppModel.ChangeOutputDevice(selectedDevice);
            AppModel.Instance.RequestSave();
        }
    }

    public override async Task OnNavigatedToAsync()
    {
        if (!IsInitialized)
        {
            InitializeViewModel();
        }
    }

    public override Task OnNavigatedFromAsync() => Task.CompletedTask;

    private void InitializeViewModel()
    {
        AppVersion = $"StreamFlow - {GetAssemblyVersion()}";

        // Silent — first visit to Settings each session checks automatically so the status
        // text (and the ability to just click "Install & Restart") is already there without
        // making the user click "Check for Updates" first. A no-op under MSIX/dev builds
        // (see UpdateService.IsInstalled).
        if (_updates.IsInstalled) _ = CheckForUpdatesAsync();

        InitializeStreamDeckSettings();

        IsInitialized = true;
    }

    private static string GetAssemblyVersion() => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty;

    public static async Task RefreshDevicesAsync()
    {
        try
        {
            AudioEngine.UpdateDevicesInfo();
            Outputs.Clear();
            foreach (var device in AudioEngine.Engine.PlaybackDevices)
            {
                Outputs.Add(new AudioDevice()
                {
                    Id = device.Id,
                    Name = device.Name,
                    IsDefault = device.IsDefault,
                });
            }
        }
        catch { }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        UpdateStatusText = "Checking for updates…";
        var result = await _updates.CheckForUpdatesAsync();
        UpdateStatusText = result.Status switch
        {
            UpdateCheckStatus.UpdateAvailable => $"Update available: v{result.Info!.TargetFullRelease.Version}",
            UpdateCheckStatus.UpToDate => "Up to date",
            UpdateCheckStatus.NotSupported => "Not applicable to this build",
            _ => $"Update check failed: {result.ErrorMessage}"
        };
        _pendingUpdate = result.Status == UpdateCheckStatus.UpdateAvailable ? result.Info : null;
        InstallUpdateCommand.NotifyCanExecuteChanged();
    }

    private bool CanInstallUpdate => _pendingUpdate is not null;

    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    private async Task InstallUpdateAsync()
    {
        if (_pendingUpdate is null) return;
        UpdateStatusText = "Downloading update…";
        try
        {
            // Does not return on success — Velopack restarts the process into the new version.
            await _updates.DownloadAndApplyAsync(_pendingUpdate);
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"Update failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private static async Task OpenImageCacheFolder()
    {
        Process.Start("explorer.exe", @$"{CacheManager.VisualizationCacheFolder}");
        await Task.CompletedTask;
    }

    [RelayCommand]
    private static async Task ClearImageCache()
    {
        try
        {
            if (App.Services.GetService(typeof(Services.IDialogService)) is Services.IDialogService dlg)
            {
                var confirm = await dlg.ConfirmAsync("Clear Image Cache", "Delete all cached images?", "Clear", "Cancel");
                if (!confirm)
                {
                    return;
                }
            }
        }
        catch { }

        try
        {
            CacheManager.ClearAllImages();
        }
        catch { }
    }

}
