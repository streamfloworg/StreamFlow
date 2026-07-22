using System.Windows.Threading;

namespace StreamFlow.App.ViewModels.Pages;

/// <summary>Drives every Timer overlay slot's once-a-second re-render. All the actual timer
/// math/state lives on SourceSlot and SceneEditorViewModel (TickTimerOverlaysAsync) — this file
/// only owns the ticking clock itself, mirroring how GoLiveViewModel.Chat.cs only owns the chat
/// connection lifecycle rather than any rendering logic.</summary>
public partial class GoLiveViewModel
{
    private readonly DispatcherTimer _timerOverlayTicker = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _viewerCountTicker = new() { Interval = TimeSpan.FromSeconds(30) };

    /// <summary>Always running once started — a no-op scan when no Timer overlay slot is
    /// currently running, so there's no separate start/stop lifecycle to manage.</summary>
    private void InitializeTimerOverlayTicker()
    {
        _timerOverlayTicker.Tick += async (_, _) => await SceneEditor.TickTimerOverlaysAsync();
        _timerOverlayTicker.Start();
    }

    private void InitializeViewerCountTicker()
    {
        _viewerCountTicker.Tick += async (_, _) => await UpdateViewerCountAsync();
        _viewerCountTicker.Start();
    }

    private async System.Threading.Tasks.Task UpdateViewerCountAsync()
    {
        if (!IsStreaming || ActiveProfile is null || !ActiveProfile.IsConnected)
        {
            return;
        }

        try
        {
            int? count = null;
            if (ActiveProfile.ServiceKind == StreamServiceKind.Twitch)
            {
                var token = _twitchAuth.GetAccessToken();
                var userId = ActiveProfile.ConnectedUserId;
                if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(userId))
                {
                    count = await _twitchAuth.TryFetchViewerCountAsync(token, userId);
                }
            }
            else if (ActiveProfile.ServiceKind == StreamServiceKind.YouTube)
            {
                var token = await _youTubeAuth.GetAccessTokenAsync();
                if (!string.IsNullOrEmpty(token))
                {
                    count = await _youTubeAuth.TryFetchViewerCountAsync(token);
                }
            }

            if (count.HasValue)
            {
                ViewerCount = count.Value;
            }
        }
        catch
        {
            // Ignore API exceptions during periodic check
        }
    }
}
