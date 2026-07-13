using Microsoft.Extensions.Logging;

using Velopack;
using Velopack.Sources;

namespace StreamFlow.App.Services;

public enum UpdateCheckStatus { NotSupported, UpToDate, UpdateAvailable, Error }

public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Info = null, string? ErrorMessage = null);

/// <summary>Checks for and applies new releases via Velopack, from GitHub Releases on the
/// public streamfloworg/StreamFlow repo. Only meaningful for the unpackaged distribution — the
/// MSIX build manages its own updates, and <see cref="UpdateManager.IsInstalled"/> is false for
/// both the MSIX build and a plain `dotnet run` dev session, so every method here degrades to
/// <see cref="UpdateCheckStatus.NotSupported"/>/no-op in those cases rather than erroring.</summary>
public sealed class UpdateService
{
    private static readonly UpdateManager Manager = new(new GithubSource("https://github.com/streamfloworg/StreamFlow", null, false));

    private readonly ILogger<UpdateService> _logger;

    public UpdateService(ILogger<UpdateService> logger) => _logger = logger;

    public bool IsInstalled => Manager.IsInstalled;

    public async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        if (!Manager.IsInstalled) return new UpdateCheckResult(UpdateCheckStatus.NotSupported);

        try
        {
            var info = await Manager.CheckForUpdatesAsync();
            return info is null
                ? new UpdateCheckResult(UpdateCheckStatus.UpToDate)
                : new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, info);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update check failed");
            return new UpdateCheckResult(UpdateCheckStatus.Error, ErrorMessage: ex.Message);
        }
    }

    /// <summary>Downloads the update and restarts into it — does not return normally on
    /// success, since Velopack replaces the running process.</summary>
    public async Task DownloadAndApplyAsync(UpdateInfo info, Action<int>? onProgress = null)
    {
        await Manager.DownloadUpdatesAsync(info, onProgress);
        Manager.ApplyUpdatesAndRestart(info);
    }
}
