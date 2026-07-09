using System.Windows.Threading;

namespace StreamFlow.App.ViewModels.Pages;

/// <summary>Drives every Timer overlay slot's once-a-second re-render. All the actual timer
/// math/state lives on SourceSlot and SceneEditorViewModel (TickTimerOverlaysAsync) — this file
/// only owns the ticking clock itself, mirroring how GoLiveViewModel.Chat.cs only owns the chat
/// connection lifecycle rather than any rendering logic.</summary>
public partial class GoLiveViewModel
{
    private readonly DispatcherTimer _timerOverlayTicker = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>Always running once started — a no-op scan when no Timer overlay slot is
    /// currently running, so there's no separate start/stop lifecycle to manage.</summary>
    private void InitializeTimerOverlayTicker()
    {
        _timerOverlayTicker.Tick += async (_, _) => await SceneEditor.TickTimerOverlaysAsync();
        _timerOverlayTicker.Start();
    }
}
