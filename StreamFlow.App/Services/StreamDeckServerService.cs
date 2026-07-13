using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using StreamFlow.App.ViewModels.Pages;

namespace StreamFlow.App.Services;

/// <summary>Self-hosted HTTP+WebSocket server answering the StreamFlow.StreamDeck plugin's
/// already-existing client contract (streamflow-client.ts) — that plugin specs a full API
/// (`/api/streaming/...`, `/api/scenes/...`, `/api/audio/...`, a `/ws` broadcast) but there was
/// previously no server anywhere in this app for it to actually talk to. Scoped to what
/// StreamFlow actually does today; the plugin's media-player/Spotify endpoints are deliberately
/// not implemented here (no such integration exists in this app at all) — see the Backlog
/// Roadmap plan (Obsidian vault) for the full scope decision. Recording is exposed as "record
/// while live" only, not standalone — see GoLiveViewModel.Recording.cs's own doc comment for why
/// (the native encoder currently requires a real RTMP target to start at all).
///
/// Every handler below marshals onto the WPF UI thread before touching any ViewModel — Kestrel
/// serves requests on threadpool threads, and GoLiveViewModel/SceneEditorViewModel are not
/// thread-safe (same discipline CoreBridgeService.EventReceived handlers already follow).</summary>
public sealed class StreamDeckServerService : IHostedService
{
    private readonly GoLiveViewModel _goLive;
    private readonly GoLiveSettingsService _settingsService;
    private readonly EventBus _eventBus;
    private readonly ILogger<StreamDeckServerService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private WebApplication? _app;
    private readonly List<WebSocket> _sockets = [];
    private readonly Lock _socketsLock = new();

    public bool IsRunning => _app is not null;
    public int Port { get; private set; }
    public string ApiKey { get; private set; } = "";

    public StreamDeckServerService(GoLiveViewModel goLive, GoLiveSettingsService settingsService, EventBus eventBus, ILogger<StreamDeckServerService> logger)
    {
        _goLive = goLive;
        _settingsService = settingsService;
        _eventBus = eventBus;
        _logger = logger;

        // Push state to connected Stream Deck clients as it changes, rather than making them
        // poll — see EventBus's own doc comment for why this goes through the bus rather than
        // GoLiveViewModel/SceneEditorViewModel needing to know this server exists at all.
        _eventBus.Subscribe<GoLiveStartedEvent>(_ => BroadcastAsync("streamState", BuildStreamStateOnUiThread()));
        _eventBus.Subscribe<GoLiveStoppedEvent>(_ => BroadcastAsync("streamState", BuildStreamStateOnUiThread()));
        _eventBus.Subscribe<SceneSwitchedEvent>(_ => BroadcastAsync("sceneState", BuildSceneStateOnUiThread()));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var saved = _settingsService.Load();
        if (!saved.IsStreamDeckServerEnabled) return;
        await StartServerAsync(saved.StreamDeckServerPort, saved.StreamDeckApiKey);
    }

    public Task StopAsync(CancellationToken cancellationToken) => StopServerAsync();

    /// <summary>Starts (stopping any already-running instance first) on the given port — called
    /// both at app startup (if enabled in settings) and from the Settings page when the user
    /// toggles the server on or changes its port.</summary>
    public async Task StartServerAsync(int port, string? apiKey)
    {
        await StopServerAsync();

        ApiKey = string.IsNullOrWhiteSpace(apiKey) ? GenerateAndPersistApiKey() : apiKey;
        Port = port;

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders(); // This app has its own tracing/log setup already.
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, port));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

        var app = builder.Build();
        app.UseWebSockets();
        app.Use(AuthenticateAsync);
        MapEndpoints(app);

        try
        {
            await app.StartAsync();
            _app = app;
            _logger.LogInformation("Stream Deck server listening on http://127.0.0.1:{Port}", port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stream Deck server failed to start on port {Port} (already in use?)", port);
            await app.DisposeAsync();
        }
    }

    public async Task StopServerAsync()
    {
        if (_app is null) return;
        var app = _app;
        _app = null;
        await app.StopAsync();
        await app.DisposeAsync();

        List<WebSocket> sockets;
        lock (_socketsLock)
        {
            sockets = [.. _sockets];
            _sockets.Clear();
        }
        foreach (var socket in sockets)
        {
            try { socket.Dispose(); } catch { /* already gone */ }
        }
    }

    private string GenerateAndPersistApiKey()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var saved = _settingsService.Load();
        saved.StreamDeckApiKey = key;
        _settingsService.Save(saved);
        return key;
    }

    /// <summary>Rotates the key immediately — safe whether the server is currently running or
    /// not, since AuthenticateAsync reads the <see cref="ApiKey"/> property live on every
    /// request rather than capturing it at startup, so no restart is needed for this to take
    /// effect. Called from the Settings page's "Regenerate" button.</summary>
    public string RegenerateApiKey() => ApiKey = GenerateAndPersistApiKey();

    /// <summary>WebSocket clients can't set custom headers during the upgrade handshake, so /ws
    /// auth is via a query-string token instead (matching the plugin's own
    /// `wsUrl += "?token=" + apiKey` construction) — every other route uses the standard
    /// Authorization: Bearer header.</summary>
    private async Task AuthenticateAsync(HttpContext context, RequestDelegate next)
    {
        var isWebSocketRoute = context.Request.Path == "/ws";
        var provided = isWebSocketRoute
            ? context.Request.Query["token"].ToString()
            : context.Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(ApiKey) || provided != ApiKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }

    // ── Endpoints ────────────────────────────────────────────────────────────────

    private void MapEndpoints(WebApplication app)
    {
        app.MapPost("/api/streaming/toggle", async (StreamingToggleRequest? req) => await RunOnUiThreadAsync(async () =>
        {
            if (!string.IsNullOrEmpty(req?.Platform) && !string.Equals(req.Platform, "all", StringComparison.OrdinalIgnoreCase))
                _logger.LogWarning("Stream Deck requested platform '{Platform}' — StreamFlow only has one active streaming profile at a time; ignoring", req.Platform);

            if (_goLive.IsStreaming)
            {
                if (_goLive.StopStreamCommand.CanExecute(null))
                    await _goLive.StopStreamCommand.ExecuteAsync(null);
            }
            else if (_goLive.StartStreamCommand.CanExecute(null))
            {
                await _goLive.StartStreamCommand.ExecuteAsync(null);
            }
            else
            {
                return Results.BadRequest(new { error = "Cannot start stream right now (no active profile, or already busy)" });
            }
            return Results.Ok(BuildStreamState());
        }));

        app.MapGet("/api/streaming/status", () => RunOnUiThreadAsync(() => Results.Ok(BuildStreamState())));

        // Toggles the "record while live" checkbox itself, not an in-progress recording — see
        // this class's own doc comment for why recording can't be started/stopped independently
        // of a stream. If already live, this only takes effect on the *next* stream.
        app.MapPost("/api/recording/toggle", (RecordingToggleRequest? req) => RunOnUiThreadAsync(() =>
        {
            _goLive.IsRecordingEnabled = !_goLive.IsRecordingEnabled;
            return Results.Ok(BuildStreamState());
        }));

        app.MapPost("/api/audio/mute/toggle", (AudioSourceRequest req) => RunOnUiThreadAsync(() =>
        {
            var item = _goLive.AudioSources.FirstOrDefault(a => a.Device.Id == req.Source);
            if (item is null) return Results.NotFound(new { error = $"No audio source '{req.Source}'" });
            item.IsMuted = !item.IsMuted;
            return Results.Ok(BuildAudioState(item));
        }));

        app.MapPost("/api/audio/volume", (AudioVolumeRequest req) => RunOnUiThreadAsync(() =>
        {
            var item = _goLive.AudioSources.FirstOrDefault(a => a.Device.Id == req.Source);
            if (item is null) return Results.NotFound(new { error = $"No audio source '{req.Source}'" });
            item.VolumePercent = req.Mode switch
            {
                "increment" => Math.Clamp(item.VolumePercent + req.Value, 0, 100),
                "decrement" => Math.Clamp(item.VolumePercent - req.Value, 0, 100),
                _ => Math.Clamp(req.Value, 0, 100),
            };
            return Results.Ok(BuildAudioState(item));
        }));

        app.MapGet("/api/audio/status", () => RunOnUiThreadAsync(() =>
            Results.Ok(_goLive.AudioSources.Select(BuildAudioState).ToList())));

        app.MapGet("/api/scenes", () => RunOnUiThreadAsync(() => Results.Ok(BuildSceneState())));

        app.MapPost("/api/scenes/switch", (SceneSwitchRequest req) => RunOnUiThreadAsync(() =>
        {
            // durationMs (a per-switch transition-duration override) isn't supported yet — every
            // switch uses whatever SceneEditor.TransitionDurationMs is currently configured to.
            var scene = _goLive.SceneEditor.Scenes.FirstOrDefault(s => s.Id == req.Scene)
                ?? _goLive.SceneEditor.Scenes.FirstOrDefault(s => string.Equals(s.Name, req.Scene, StringComparison.OrdinalIgnoreCase));
            if (scene is null) return Results.NotFound(new { error = $"No scene '{req.Scene}'" });
            _goLive.SceneEditor.ActiveScene = scene;
            return Results.Ok(BuildSceneState());
        }));

        app.MapPost("/api/scenes/next", (SceneCycleRequest? req) => RunOnUiThreadAsync(() => Results.Ok(CycleScene(1))));
        app.MapPost("/api/scenes/prev", (SceneCycleRequest? req) => RunOnUiThreadAsync(() => Results.Ok(CycleScene(-1))));

        app.Map("/ws", HandleWebSocketAsync);
    }

    private SceneStateResponse CycleScene(int direction)
    {
        var scenes = _goLive.SceneEditor.Scenes;
        if (scenes.Count == 0) return BuildSceneState();

        var currentIndex = _goLive.SceneEditor.ActiveScene is { } active ? scenes.IndexOf(active) : -1;
        var nextIndex = ((currentIndex + direction) % scenes.Count + scenes.Count) % scenes.Count;
        _goLive.SceneEditor.ActiveScene = scenes[nextIndex];
        return BuildSceneState();
    }

    // ── WebSocket ────────────────────────────────────────────────────────────────

    private async Task HandleWebSocketAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var socket = await context.WebSockets.AcceptWebSocketAsync();
        lock (_socketsLock) { _sockets.Add(socket); }

        try
        {
            // Hydrate the newly-connected client immediately rather than leaving it blank until
            // the next state change.
            await SendAsync(socket, "streamState", await RunOnUiThreadAsync(BuildStreamState));
            await SendAsync(socket, "sceneState", await RunOnUiThreadAsync(BuildSceneState));

            var buffer = new byte[1024];
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;
                // Inbound messages aren't part of the plugin's contract (state changes only ever
                // flow app → plugin) — just drain whatever arrives so the socket doesn't back up.
            }
        }
        catch (WebSocketException)
        {
            // Client disconnected uncleanly — same cleanup as a graceful close, below.
        }
        finally
        {
            lock (_socketsLock) { _sockets.Remove(socket); }
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); }
                catch { /* best-effort */ }
            }
            socket.Dispose();
        }
    }

    private async Task BroadcastAsync(string eventName, object data)
    {
        List<WebSocket> targets;
        lock (_socketsLock) { targets = [.. _sockets]; }

        foreach (var socket in targets)
            await SendAsync(socket, eventName, data);
    }

    private static async Task SendAsync(WebSocket socket, string eventName, object data)
    {
        if (socket.State != WebSocketState.Open) return;
        try
        {
            var json = JsonSerializer.Serialize(new { @event = eventName, data }, JsonOpts);
            await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch
        {
            // Socket died mid-send — HandleWebSocketAsync's own receive loop will notice and
            // clean it up; nothing more to do here.
        }
    }

    // ── UI-thread marshaling ────────────────────────────────────────────────────

    private static Task<T> RunOnUiThreadAsync<T>(Func<T> func)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            return Task.FromResult(func());
        return dispatcher.InvokeAsync(func).Task;
    }

    private static Task<T> RunOnUiThreadAsync<T>(Func<Task<T>> func)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            return func();
        return dispatcher.InvokeAsync(func).Task.Unwrap();
    }

    private StreamStateResponse BuildStreamStateOnUiThread() => RunOnUiThreadAsync(BuildStreamState).Result;
    private SceneStateResponse BuildSceneStateOnUiThread() => RunOnUiThreadAsync(BuildSceneState).Result;

    // ── DTOs (camelCase over the wire — see JsonOpts/ConfigureHttpJsonOptions) ─────

    private StreamStateResponse BuildStreamState() =>
        // ViewerCount stubbed at 0 — no Twitch Helix/YouTube Live viewer-count polling exists
        // anywhere in this app yet; see the Backlog Roadmap plan for why that's deliberately out
        // of scope here rather than half-implemented.
        new(_goLive.IsStreaming, _goLive.IsStreaming && _goLive.IsRecordingEnabled, ViewerCount: 0);

    private static AudioStateResponse BuildAudioState(AudioSourceItem item) =>
        new(item.Device.Id, item.VolumePercent, item.IsMuted);

    private SceneStateResponse BuildSceneState() =>
        new(_goLive.SceneEditor.ActiveScene?.Id,
            _goLive.SceneEditor.Scenes.Select(s => new SceneResponse(s.Id, s.Name)).ToList());

    private sealed record StreamingToggleRequest(string? Platform);
    private sealed record RecordingToggleRequest(string? Type);
    private sealed record AudioSourceRequest(string Source);
    private sealed record AudioVolumeRequest(string Source, double Value, string Mode = "set");
    private sealed record SceneSwitchRequest(string Scene, int? DurationMs);
    private sealed record SceneCycleRequest(int? DurationMs);

    private sealed record StreamStateResponse(bool Streaming, bool Recording, int ViewerCount);
    private sealed record AudioStateResponse(string Source, double Volume, bool Muted);
    private sealed record SceneResponse(string Id, string Name);
    private sealed record SceneStateResponse(string? ActiveSceneId, List<SceneResponse> Scenes);
}
