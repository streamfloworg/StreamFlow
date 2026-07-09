using System.Net.WebSockets;
using System.Text;
using System.IO;

using Microsoft.Extensions.Logging;

namespace StreamFlow.App.Services;

/// <summary>
/// Reads a Twitch channel's chat via anonymous, read-only IRC-over-WebSocket — Twitch allows
/// connecting with a "justinfanNNNNN" nick and no token at all for this, so no OAuth scope
/// changes were needed on top of what TwitchAuthService already requests (which is only used to
/// identify the connected account, not to read chat).
/// </summary>
public sealed class TwitchChatService
{
    private const string WebSocketUri = "wss://irc-ws.chat.twitch.tv:443";

    private readonly ILogger<TwitchChatService> _logger;
    private CancellationTokenSource? _runCts;
    private Task? _runTask;

    public event EventHandler<ChatMessage>? MessageReceived;

    public TwitchChatService(ILogger<TwitchChatService> logger)
    {
        _logger = logger;
    }

    /// <summary>Starts (or restarts, if already running for a different channel) reading chat
    /// for the given channel login name.</summary>
    public void Start(string channelLogin)
    {
        Stop();

        var cts = new CancellationTokenSource();
        _runCts = cts;
        _runTask = RunAsync(channelLogin.ToLowerInvariant(), cts.Token);
    }

    public void Stop()
    {
        _runCts?.Cancel();
        _runCts = null;
        _runTask = null;
    }

    private async Task RunAsync(string channelLogin, CancellationToken ct)
    {
        using var socket = new ClientWebSocket();
        try
        {
            await socket.ConnectAsync(new Uri(WebSocketUri), ct);

            var nick = $"justinfan{Random.Shared.Next(10000, 99999)}";
            await SendAsync(socket, "CAP REQ :twitch.tv/tags", ct);
            await SendAsync(socket, "PASS SCHMOOPIIE", ct);
            await SendAsync(socket, $"NICK {nick}", ct);
            await SendAsync(socket, $"JOIN #{channelLogin}", ct);

            var buffer = new byte[8192];
            var lineBuilder = new StringBuilder();

            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;

                lineBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage) continue;

                var chunk = lineBuilder.ToString();
                lineBuilder.Clear();

                foreach (var line in chunk.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
                    await HandleLineAsync(socket, line, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is WebSocketException or IOException)
        {
            _logger.LogWarning(ex, "Twitch chat connection to #{Channel} failed", channelLogin);
        }
    }

    private async Task HandleLineAsync(ClientWebSocket socket, string line, CancellationToken ct)
    {
        if (line.StartsWith("PING", StringComparison.Ordinal))
        {
            await SendAsync(socket, "PONG :tmi.twitch.tv", ct);
            return;
        }

        var parsed = ParsePrivmsg(line);
        if (parsed is not null)
            MessageReceived?.Invoke(this, parsed);
    }

    /// <summary>
    /// Parses a tagged IRCv3 PRIVMSG line, e.g.:
    /// <c>@color=#FF0000;display-name=Someone :someone!someone@someone.tmi.twitch.tv PRIVMSG #channel :hello there</c>
    /// Returns null for any other kind of line (JOIN acks, NOTICE, etc.).
    /// </summary>
    internal static ChatMessage? ParsePrivmsg(string line)
    {
        var tags = new Dictionary<string, string>();
        var rest = line;

        if (rest.StartsWith('@'))
        {
            var spaceIdx = rest.IndexOf(' ');
            if (spaceIdx < 0) return null;
            foreach (var tag in rest[1..spaceIdx].Split(';'))
            {
                var eq = tag.IndexOf('=');
                if (eq > 0) tags[tag[..eq]] = tag[(eq + 1)..];
            }
            rest = rest[(spaceIdx + 1)..];
        }

        // rest is now ":nick!user@host PRIVMSG #channel :message text"
        if (!rest.StartsWith(':')) return null;
        var prefixEnd = rest.IndexOf(' ');
        if (prefixEnd < 0) return null;
        var prefix = rest[1..prefixEnd];
        rest = rest[(prefixEnd + 1)..];

        if (!rest.StartsWith("PRIVMSG ", StringComparison.Ordinal)) return null;
        var textIdx = rest.IndexOf(" :", StringComparison.Ordinal);
        if (textIdx < 0) return null;
        var text = rest[(textIdx + 2)..];

        var username = tags.TryGetValue("display-name", out var displayName) && !string.IsNullOrEmpty(displayName)
            ? displayName
            : prefix.Split('!')[0];

        var colorHex = tags.TryGetValue("color", out var color) && !string.IsNullOrEmpty(color) ? color : null;

        return new ChatMessage(username, text, colorHex);
    }

    private static async Task SendAsync(ClientWebSocket socket, string line, CancellationToken ct) =>
        await socket.SendAsync(Encoding.UTF8.GetBytes(line + "\r\n"), WebSocketMessageType.Text, true, ct);
}
