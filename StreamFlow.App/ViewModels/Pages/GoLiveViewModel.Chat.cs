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
        var hasChatSlot = SceneEditor.Slots.Any(s => s.IsChatOverlay);
        if (!hasChatSlot)
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
}
