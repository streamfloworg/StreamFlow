using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace StreamFlow.App.Services;

public sealed class YouTubeChatService
{
    private readonly YouTubeAuthService _authService;
    private readonly ILogger<YouTubeChatService> _logger;
    private readonly HttpClient _http = new();

    private CancellationTokenSource? _runCts;
    private Task? _runTask;

    public event EventHandler<ChatMessage>? MessageReceived;

    public YouTubeChatService(YouTubeAuthService authService, ILogger<YouTubeChatService> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    public void Start()
    {
        Stop();

        var cts = new CancellationTokenSource();
        _runCts = cts;
        _runTask = RunAsync(cts.Token);
    }

    public void Stop()
    {
        _runCts?.Cancel();
        _runCts = null;
        _runTask = null;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            string? liveChatId = null;
            string? nextPageToken = null;

            while (!ct.IsCancellationRequested)
            {
                var token = await _authService.GetAccessTokenAsync(ct);
                if (token is null)
                {
                    _logger.LogWarning("YouTube Chat: No access token available. Retrying in 10s...");
                    await Task.Delay(10000, ct);
                    continue;
                }

                if (liveChatId is null)
                {
                    liveChatId = await FetchActiveChatIdAsync(token, ct);
                    if (liveChatId is null)
                    {
                        _logger.LogInformation("YouTube Chat: No active/upcoming live stream found. Retrying in 10s...");
                        await Task.Delay(10000, ct);
                        continue;
                    }
                    _logger.LogInformation("YouTube Chat: Connected to live chat ID: {LiveChatId}", liveChatId);
                    // On first connection, perform a seeding request to get the initial nextPageToken
                    // and discard history to avoid displaying old chat messages on overlay startup.
                    nextPageToken = await GetInitialPageTokenAsync(token, liveChatId, ct);
                }

                var pollResult = await PollChatMessagesAsync(token, liveChatId, nextPageToken, ct);
                if (pollResult is null)
                {
                    // Failed to poll (could be network or stream ended/deleted). Reset liveChatId to re-detect.
                    _logger.LogWarning("YouTube Chat: Poll failed. Resetting connection...");
                    liveChatId = null;
                    nextPageToken = null;
                    await Task.Delay(5000, ct);
                    continue;
                }

                nextPageToken = pollResult.Value.NextPageToken;

                foreach (var msg in pollResult.Value.Messages)
                {
                    MessageReceived?.Invoke(this, msg);
                }

                var delayMs = Math.Max(2000, pollResult.Value.PollingIntervalMs);
                await Task.Delay(delayMs, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "YouTube Chat error");
        }
    }

    private async Task<string?> FetchActiveChatIdAsync(string token, CancellationToken ct)
    {
        // 1. Try active broadcasts first
        var chatId = await QueryBroadcastChatIdAsync(token, "active", ct);
        if (chatId is not null) return chatId;

        // 2. Fallback to upcoming broadcasts
        return await QueryBroadcastChatIdAsync(token, "upcoming", ct);
    }

    private async Task<string?> QueryBroadcastChatIdAsync(string token, string status, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://www.googleapis.com/youtube/v3/liveBroadcasts?part=snippet&broadcastStatus={status}&maxResults=1");
            req.Headers.Authorization = new("Bearer", token);

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));
            if (doc.RootElement.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
            {
                var snippet = items[0].GetProperty("snippet");
                if (snippet.TryGetProperty("liveChatId", out var chatProp))
                {
                    return chatProp.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query YouTube broadcasts for status: {Status}", status);
        }
        return null;
    }

    private async Task<string?> GetInitialPageTokenAsync(string token, string liveChatId, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://www.googleapis.com/youtube/v3/liveChatMessages?part=snippet&liveChatId={liveChatId}&maxResults=1");
            req.Headers.Authorization = new("Bearer", token);

            using var resp = await _http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));
                if (doc.RootElement.TryGetProperty("nextPageToken", out var pageTokenProp))
                {
                    return pageTokenProp.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get initial YouTube page token");
        }
        return null;
    }

    private async Task<(string NextPageToken, int PollingIntervalMs, List<ChatMessage> Messages)?> PollChatMessagesAsync(
        string token, string liveChatId, string? pageToken, CancellationToken ct)
    {
        try
        {
            var url = $"https://www.googleapis.com/youtube/v3/liveChatMessages?part=authorDetails,snippet&liveChatId={liveChatId}";
            if (!string.IsNullOrEmpty(pageToken))
            {
                url += $"&pageToken={pageToken}";
            }

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new("Bearer", token);

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));
            var root = doc.RootElement;

            var nextToken = root.GetProperty("nextPageToken").GetString() ?? pageToken ?? "";
            var interval = root.TryGetProperty("pollingIntervalMillis", out var intProp) ? intProp.GetInt32() : 4000;

            var messages = new List<ChatMessage>();
            if (root.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    var authorDetails = item.GetProperty("authorDetails");
                    var snippet = item.GetProperty("snippet");

                    var displayName = authorDetails.GetProperty("displayName").GetString() ?? "YouTube User";
                    var text = snippet.GetProperty("displayMessage").GetString() ?? "";

                    messages.Add(new ChatMessage(displayName, text, null));
                }
            }

            return (nextToken, interval, messages);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to poll YouTube chat messages");
            return null;
        }
    }
}
