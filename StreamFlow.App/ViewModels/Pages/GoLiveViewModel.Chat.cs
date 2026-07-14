using StreamFlow.App.Services;

namespace StreamFlow.App.ViewModels.Pages;

/// <summary>Ties chat overlay slots to the connected streaming service's chat (Twitch/YouTube).
/// The chat overlay slot itself is created/rendered in the shared SceneEditorViewModel
/// (AddChatOverlayAsync) — this file only owns starting/stopping the underlying chat
/// connection and feeding incoming messages back into whichever slots are chat overlays.</summary>
public partial class GoLiveViewModel
{
    private void UpdateChatConnection()
    {
        var shouldConnect = SceneEditor.Slots.Any(s => s.IsChatOverlay) || (ActiveProfile is not null && ActiveProfile.IsConnected);
        if (!shouldConnect)
        {
            _twitchChat.Stop();
            _youTubeChat.Stop();
            return;
        }

        if (ActiveProfile is null) return;

        if (ActiveProfile.ServiceKind == StreamServiceKind.Twitch && ActiveProfile.IsConnected)
        {
            _youTubeChat.Stop();
            var channel = ActiveProfile.ConnectedAccountLabel;
            if (!string.IsNullOrEmpty(channel))
            {
                _twitchChat.Start(channel);
            }
        }
        else if (ActiveProfile.ServiceKind == StreamServiceKind.YouTube && ActiveProfile.IsConnected)
        {
            _twitchChat.Stop();
            _youTubeChat.Start();
        }
        else
        {
            _twitchChat.Stop();
            _youTubeChat.Stop();
        }
    }

    private void OnChatMessageReceived(object? sender, ChatMessage message)
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            LiveChatMessages.Add(message);
            while (LiveChatMessages.Count > 100)
            {
                LiveChatMessages.RemoveAt(0);
            }

            if (SceneEditor.ActiveScene is null) return;
            var chatSlots = SceneEditor.ActiveScene.Slots.Where(s => s.Content is ChatOverlayContent).ToList();
            if (chatSlots.Count == 0) return;

            foreach (var slot in chatSlots)
            {
                var chat = (ChatOverlayContent)slot.Content!;
                // A real message always displaces placeholder content outright rather than
                // appending after it — see ChatOverlayContent.IsShowingPlaceholder.
                if (chat.IsShowingPlaceholder)
                {
                    chat.ChatMessages.Clear();
                    chat.IsShowingPlaceholder = false;
                }

                chat.ChatMessages.Add(message);
                while (chat.ChatMessages.Count > 10)
                {
                    chat.ChatMessages.RemoveAt(0);
                }

                SceneEditor.ScheduleOverlayContentUpdate(slot);
            }
        });
    }

    [ObservableProperty]
    private string _activeSidebarTab = "Settings"; // "Settings" or "Manager"

    public bool IsSettingsTabActive
    {
        get => ActiveSidebarTab == "Settings";
        set
        {
            if (value)
            {
                ActiveSidebarTab = "Settings";
            }
        }
    }

    public bool IsManagerTabActive
    {
        get => ActiveSidebarTab == "Manager";
        set
        {
            if (value)
            {
                ActiveSidebarTab = "Manager";
            }
        }
    }

    partial void OnActiveSidebarTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsSettingsTabActive));
        OnPropertyChanged(nameof(IsManagerTabActive));
    }

    [ObservableProperty]
    private string _streamTitle = "";

    [ObservableProperty]
    private string _streamCategory = ""; // In Twitch this is category/game, in YouTube this serves as description

    [ObservableProperty]
    private string _updateInfoStatus = "";

    public System.Collections.ObjectModel.ObservableCollection<ChatMessage> LiveChatMessages { get; } = [];

    [RelayCommand]
    private async Task UpdateStreamInfoAsync()
    {
        if (ActiveProfile is null || !ActiveProfile.IsConnected)
        {
            UpdateInfoStatus = "Error: Not connected to any service";
            return;
        }

        UpdateInfoStatus = "Updating...";
        bool success = false;

        if (ActiveProfile.ServiceKind == StreamServiceKind.Twitch)
        {
            var token = _twitchAuth.GetAccessToken();
            var userId = ActiveProfile.ConnectedUserId;
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(userId))
            {
                UpdateInfoStatus = "Error: Twitch credentials not found. Reconnect required.";
                return;
            }
            success = await _twitchAuth.UpdateStreamInfoAsync(token, userId, StreamTitle, StreamCategory);
        }
        else if (ActiveProfile.ServiceKind == StreamServiceKind.YouTube)
        {
            var token = await _youTubeAuth.GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                UpdateInfoStatus = "Error: YouTube credentials not found. Reconnect required.";
                return;
            }
            success = await _youTubeAuth.UpdateStreamInfoAsync(token, StreamTitle, StreamCategory);
        }
        else
        {
            UpdateInfoStatus = "Updating stream info is not supported for Custom RTMP.";
            return;
        }

        UpdateInfoStatus = success ? "Success: Stream info updated!" : "Error: Failed to update stream info";
    }
}
