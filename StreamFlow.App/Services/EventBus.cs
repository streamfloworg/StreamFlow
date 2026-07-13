namespace StreamFlow.App.Services;

/// <summary>Base type for in-process pub/sub events published via <see cref="EventBus"/> — a
/// sealed record hierarchy rather than an interface/enum so subscribers pattern-match on concrete
/// type and payloads get equality/deconstruction for free. See the Backlog Roadmap plan (Obsidian
/// vault) for the full Event System/Triggers design — this is the extensibility point scene
/// switching, Go Live, and eventually platform alerts (subscriber/follow/donation) all publish
/// through, so future consumers (Stream Deck's WebSocket broadcast, alert overlays) don't need to
/// hook into GoLiveViewModel/SceneEditorViewModel's own code directly.</summary>
public abstract record AppEvent;

/// <summary>Published once the core has confirmed the stream actually started (StreamStartedEvent
/// in GoLiveViewModel.cs) — not when StartStreamCommand is merely sent, since that can still fail
/// before the core ever goes live.</summary>
public sealed record GoLiveStartedEvent : AppEvent;

/// <summary>Published from StreamStoppedEvent handling, which fires regardless of *how* the
/// stream ended (explicit Stop, error, or core disconnect) — not just the Stop button path.</summary>
public sealed record GoLiveStoppedEvent : AppEvent;

/// <summary>Published whenever SceneEditorViewModel's ActiveScene actually changes to a real
/// scene (not the transient null used while a scene set is loading) — alongside, not instead of,
/// the existing direct call that performs the deactivate/activate/transition work. This is purely
/// an observability signal for other subscribers; the scene-switch critical path never depends on
/// anything subscribed here.</summary>
public sealed record SceneSwitchedEvent(string? FromSceneId, string ToSceneId) : AppEvent;

/// <summary>Lightweight in-process pub/sub bus — no external dependency, since every publisher
/// and subscriber lives in the same WPF process as DI singletons for the app's lifetime. Publish
/// dispatches synchronously on whatever thread called it; subscribers that need UI-thread
/// affinity are responsible for their own Dispatcher.BeginInvoke, same discipline already used for
/// every other cross-thread event in this app (see CoreBridgeService.EventReceived).</summary>
public sealed class EventBus
{
    private readonly Dictionary<Type, List<Delegate>> _subscribers = [];
    private readonly Lock _lock = new();

    /// <summary>Registers a handler for one AppEvent type. Returns an IDisposable to unsubscribe;
    /// not strictly required today since every current subscriber is a DI singleton that lives as
    /// long as the bus itself, but kept for correctness against future non-singleton subscribers
    /// (e.g. a per-view subscription) rather than leaking a delegate reference forever.</summary>
    public IDisposable Subscribe<T>(Action<T> handler) where T : AppEvent
    {
        lock (_lock)
        {
            if (!_subscribers.TryGetValue(typeof(T), out var list))
            {
                list = [];
                _subscribers[typeof(T)] = list;
            }
            list.Add(handler);
        }
        return new Subscription(() =>
        {
            lock (_lock)
            {
                if (_subscribers.TryGetValue(typeof(T), out var list))
                    list.Remove(handler);
            }
        });
    }

    public void Publish<T>(T evt) where T : AppEvent
    {
        Delegate[] handlers;
        lock (_lock)
        {
            if (!_subscribers.TryGetValue(typeof(T), out var list) || list.Count == 0) return;
            handlers = [.. list];
        }
        foreach (var handler in handlers)
            ((Action<T>)handler).Invoke(evt);
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
