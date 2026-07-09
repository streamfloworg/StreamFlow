using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using StreamFlow.Core.Helpers;

namespace StreamFlow.App.Services;

public sealed record YouTubeAuthResult(string AccessToken, string ChannelName);

public sealed record YouTubeStreamKeyInfo(string IngestionAddress, string StreamName);

/// <summary>An ephemeral unlisted broadcast created for Test Mode — BroadcastId is only needed
/// afterward to end/delete it (see YouTubeAuthService.EndTestBroadcastAsync); the ingest info is
/// what actually gets used as the RTMP publish target.</summary>
public sealed record YouTubeTestBroadcastInfo(string BroadcastId, string IngestionAddress, string StreamName);

/// <summary>
/// YouTube sign-in via Authorization Code + PKCE (Google's recommended flow for installed
/// apps). Unlike Twitch's implicit flow, the code arrives as a normal query-string redirect,
/// so no fragment-relay page is needed. Google's "Desktop app" client type allows any
/// loopback port at runtime, so one is picked dynamically instead of a fixed registration.
/// </summary>
public sealed class YouTubeAuthService
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string Scope = "https://www.googleapis.com/auth/youtube";

    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly ILogger<YouTubeAuthService> _logger;
    private readonly HttpClient _http = new();

    private string? _cachedAccessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    private static string TokenFilePath => Path.Combine(AppDataPaths.RootFolder, "youtube_refresh_token.dat");

    public YouTubeAuthService(IConfiguration config, ILogger<YouTubeAuthService> logger)
    {
        _clientId = config["YouTube:ClientId"] ?? "";
        _clientSecret = config["YouTube:ClientSecret"] ?? "";
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_clientId) && !string.IsNullOrEmpty(_clientSecret);

    /// <summary>Tries to resume a previous session using the saved refresh token, if any.</summary>
    public async Task<YouTubeAuthResult?> TryRestoreAsync(CancellationToken ct = default)
    {
        var refreshToken = LoadRefreshToken();
        if (refreshToken is null) return null;

        var accessToken = await RefreshAccessTokenAsync(refreshToken, ct);
        return accessToken is null ? null : await FetchChannelAsync(accessToken, ct);
    }

    /// <summary>Runs the full sign-in flow: opens the system browser, waits for the redirect, exchanges the code.</summary>
    public async Task<YouTubeAuthResult?> ConnectAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) return null;

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = ComputeCodeChallenge(codeVerifier);

        using var listener = new HttpListener();
        var port = BindToFreeLoopbackPort(listener);
        var redirectUri = $"http://127.0.0.1:{port}/callback";

        var authorizeUrl =
            $"{AuthorizeEndpoint}?client_id={Uri.EscapeDataString(_clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            "&response_type=code" +
            $"&scope={Uri.EscapeDataString(Scope)}" +
            $"&code_challenge={codeChallenge}" +
            "&code_challenge_method=S256" +
            "&access_type=offline" +
            "&prompt=consent" +
            $"&state={state}";

        Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true });

        try
        {
            var context = await listener.GetContextAsync().WaitAsync(ct);
            var query = HttpUtility.ParseQueryString(context.Request.Url?.Query ?? "");
            var response = context.Response;

            var error = query["error"];
            if (error is not null)
            {
                await WriteHtmlAsync(response, ErrorHtml(error));
                return null;
            }

            var code = query["code"];
            if (query["state"] != state || string.IsNullOrEmpty(code))
            {
                await WriteHtmlAsync(response, ErrorHtml("Login response didn't match the request. Please try again."));
                return null;
            }

            var tokens = await ExchangeCodeAsync(code, codeVerifier, redirectUri, ct);
            if (tokens is null)
            {
                await WriteHtmlAsync(response, ErrorHtml("Couldn't complete the Google sign-in."));
                return null;
            }

            var result = await FetchChannelAsync(tokens.Value.AccessToken, ct);
            await WriteHtmlAsync(response, result is not null ? SuccessHtml : ErrorHtml("Signed in, but couldn't read your channel."));

            if (result is not null && tokens.Value.RefreshToken is not null)
                SaveRefreshToken(tokens.Value.RefreshToken);

            return result;
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>Looks for an existing reusable stream on the connected channel and returns its ingest URL + key, if any.</summary>
    public async Task<YouTubeStreamKeyInfo?> TryFetchStreamKeyAsync(string accessToken, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get,
            "https://www.googleapis.com/youtube/v3/liveStreams?part=cdn&mine=true&maxResults=1");
        req.Headers.Authorization = new("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
            return null;

        var cdn = items[0].GetProperty("cdn");
        var ingestionInfo = cdn.GetProperty("ingestionInfo");
        var address = ingestionInfo.GetProperty("ingestionAddress").GetString();
        var streamName = ingestionInfo.GetProperty("streamName").GetString();

        return address is null || streamName is null ? null : new YouTubeStreamKeyInfo(address, streamName);
    }

    /// <summary>Creates a throwaway unlisted broadcast bound to the account's reusable stream and
    /// returns its ingest info — StreamFlow's equivalent of Twitch's "?bandwidthtest=true" for
    /// YouTube, since YouTube's ingest has no such flag and would otherwise publish a real,
    /// publicly-listed broadcast. Caller is responsible for calling EndTestBroadcastAsync once
    /// the test stream stops, so the broadcast doesn't linger on the channel.</summary>
    public async Task<YouTubeTestBroadcastInfo?> CreateTestBroadcastAsync(string accessToken, CancellationToken ct = default)
    {
        var stream = await GetOrCreateReusableStreamAsync(accessToken, ct);
        if (stream is null) return null;

        var createBroadcastBody = JsonSerializer.Serialize(new
        {
            snippet = new
            {
                title = $"StreamFlow Test — {DateTime.Now:yyyy-MM-dd HH:mm}",
                // Required by the API even though we're going live immediately, not on a
                // schedule — "now" satisfies liveBroadcasts.insert's validation.
                scheduledStartTime = DateTime.UtcNow.ToString("o"),
            },
            status = new
            {
                privacyStatus = "unlisted",
                selfDeclaredMadeForKids = false,
            },
        });

        using var createReq = new HttpRequestMessage(HttpMethod.Post,
            "https://www.googleapis.com/youtube/v3/liveBroadcasts?part=snippet,status")
        { Content = new StringContent(createBroadcastBody, Encoding.UTF8, "application/json") };
        createReq.Headers.Authorization = new("Bearer", accessToken);

        using var createResp = await _http.SendAsync(createReq, ct);
        if (!createResp.IsSuccessStatusCode)
        {
            _logger.LogWarning("YouTube liveBroadcasts.insert failed: {Status} {Body}",
                createResp.StatusCode, await createResp.Content.ReadAsStringAsync(ct));
            return null;
        }

        using var createDoc = JsonDocument.Parse(await createResp.Content.ReadAsStreamAsync(ct));
        var broadcastId = createDoc.RootElement.GetProperty("id").GetString();
        if (broadcastId is null) return null;

        // Bind it to the reusable stream so it actually ingests whatever gets published to that
        // stream's key — without this the broadcast exists but never receives any video.
        using var bindReq = new HttpRequestMessage(HttpMethod.Post,
            $"https://www.googleapis.com/youtube/v3/liveBroadcasts/bind?id={Uri.EscapeDataString(broadcastId)}&streamId={Uri.EscapeDataString(stream.Value.Id)}&part=id,contentDetails");
        bindReq.Headers.Authorization = new("Bearer", accessToken);

        using var bindResp = await _http.SendAsync(bindReq, ct);
        if (!bindResp.IsSuccessStatusCode)
        {
            _logger.LogWarning("YouTube liveBroadcasts.bind failed: {Status} {Body}",
                bindResp.StatusCode, await bindResp.Content.ReadAsStringAsync(ct));
            return null;
        }

        return new YouTubeTestBroadcastInfo(broadcastId, stream.Value.IngestionAddress, stream.Value.StreamName);
    }

    /// <summary>Best-effort teardown for a broadcast created by CreateTestBroadcastAsync: attempts
    /// to transition it to "complete" (a no-op if it never actually went live) and then deletes
    /// it outright, since — unlike a real broadcast — nothing about a test run is worth keeping
    /// around as an unlisted video on the channel. Swallows failures; called from a stream-stopped
    /// handler that has nowhere useful to surface an error even if this fails.</summary>
    public async Task EndTestBroadcastAsync(string accessToken, string broadcastId, CancellationToken ct = default)
    {
        using var transitionReq = new HttpRequestMessage(HttpMethod.Post,
            $"https://www.googleapis.com/youtube/v3/liveBroadcasts/transition?id={Uri.EscapeDataString(broadcastId)}&broadcastStatus=complete&part=id,status");
        transitionReq.Headers.Authorization = new("Bearer", accessToken);
        try
        {
            using var transitionResp = await _http.SendAsync(transitionReq, ct);
            if (!transitionResp.IsSuccessStatusCode)
                _logger.LogWarning("YouTube liveBroadcasts.transition(complete) failed for test broadcast {Id}: {Status}", broadcastId, transitionResp.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "YouTube liveBroadcasts.transition(complete) threw for test broadcast {Id}", broadcastId);
        }

        using var deleteReq = new HttpRequestMessage(HttpMethod.Delete,
            $"https://www.googleapis.com/youtube/v3/liveBroadcasts?id={Uri.EscapeDataString(broadcastId)}");
        deleteReq.Headers.Authorization = new("Bearer", accessToken);
        try
        {
            using var deleteResp = await _http.SendAsync(deleteReq, ct);
            if (!deleteResp.IsSuccessStatusCode)
                _logger.LogWarning("YouTube liveBroadcasts.delete failed for test broadcast {Id}: {Status}", broadcastId, deleteResp.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "YouTube liveBroadcasts.delete threw for test broadcast {Id}", broadcastId);
        }
    }

    /// <summary>Reuses the account's existing reusable liveStream (same one TryFetchStreamKeyAsync
    /// surfaces for normal Go Live) if it has one, so a test run publishes through the same
    /// ingest key rather than minting a new one every time; creates one if this is the account's
    /// first-ever stream/broadcast.</summary>
    private async Task<(string Id, string IngestionAddress, string StreamName)?> GetOrCreateReusableStreamAsync(string accessToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get,
            "https://www.googleapis.com/youtube/v3/liveStreams?part=id,cdn&mine=true&maxResults=1");
        req.Headers.Authorization = new("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));
            if (doc.RootElement.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
            {
                var parsed = ParseStream(items[0]);
                if (parsed is not null) return parsed;
            }
        }

        var createBody = JsonSerializer.Serialize(new
        {
            snippet = new { title = "StreamFlow" },
            cdn = new { frameRate = "variable", ingestionType = "rtmp", resolution = "variable" },
        });

        using var createReq = new HttpRequestMessage(HttpMethod.Post,
            "https://www.googleapis.com/youtube/v3/liveStreams?part=id,snippet,cdn")
        { Content = new StringContent(createBody, Encoding.UTF8, "application/json") };
        createReq.Headers.Authorization = new("Bearer", accessToken);

        using var createResp = await _http.SendAsync(createReq, ct);
        if (!createResp.IsSuccessStatusCode)
        {
            _logger.LogWarning("YouTube liveStreams.insert failed: {Status} {Body}",
                createResp.StatusCode, await createResp.Content.ReadAsStringAsync(ct));
            return null;
        }

        using var createDoc = JsonDocument.Parse(await createResp.Content.ReadAsStreamAsync(ct));
        return ParseStream(createDoc.RootElement);
    }

    private static (string Id, string IngestionAddress, string StreamName)? ParseStream(JsonElement item)
    {
        var id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        if (id is null || !item.TryGetProperty("cdn", out var cdn) || !cdn.TryGetProperty("ingestionInfo", out var ingestionInfo))
            return null;

        var address = ingestionInfo.TryGetProperty("ingestionAddress", out var a) ? a.GetString() : null;
        var streamName = ingestionInfo.TryGetProperty("streamName", out var s) ? s.GetString() : null;
        return address is null || streamName is null ? null : (id, address, streamName);
    }

    public void Disconnect()
    {
        _cachedAccessToken = null;
        _tokenExpiry = DateTime.MinValue;
        try { File.Delete(TokenFilePath); } catch (IOException) { }
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (_cachedAccessToken is not null && DateTime.UtcNow < _tokenExpiry)
        {
            return _cachedAccessToken;
        }

        var refreshToken = LoadRefreshToken();
        if (refreshToken is null) return null;

        return await RefreshAccessTokenAsync(refreshToken, ct);
    }

    private async Task<(string AccessToken, string? RefreshToken)?> ExchangeCodeAsync(
        string code, string codeVerifier, string redirectUri, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = codeVerifier,
        };

        using var resp = await _http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("YouTube token exchange failed: {Status} {Body}", resp.StatusCode, body);
            return null;
        }

        using var doc = JsonDocument.Parse(body);
        var accessToken = doc.RootElement.GetProperty("access_token").GetString();
        var refreshToken = doc.RootElement.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;

        if (accessToken is not null)
        {
            _cachedAccessToken = accessToken;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);
        }

        return accessToken is null ? null : (accessToken, refreshToken);
    }

    private async Task<string?> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["grant_type"] = "refresh_token",
        };

        using var resp = await _http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));
        var accessToken = doc.RootElement.GetProperty("access_token").GetString();
        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;

        if (accessToken is not null)
        {
            _cachedAccessToken = accessToken;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);
        }

        return accessToken;
    }

    private async Task<YouTubeAuthResult?> FetchChannelAsync(string accessToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true");
        req.Headers.Authorization = new("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("YouTube channels.list failed: {Status} {Body}", resp.StatusCode, body);
            return null;
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
        {
            _logger.LogWarning("YouTube channels.list returned no channels for this account: {Body}", body);
            return null;
        }

        var title = items[0].GetProperty("snippet").GetProperty("title").GetString();
        return title is null ? null : new YouTubeAuthResult(accessToken, title);
    }

    private static int BindToFreeLoopbackPort(HttpListener listener)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var port = Random.Shared.Next(49152, 65535);
            listener.Prefixes.Clear();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                listener.Start();
                return port;
            }
            catch (HttpListenerException)
            {
                // Port taken — try another.
            }
        }

        throw new InvalidOperationException("Could not find a free loopback port for the YouTube sign-in redirect.");
    }

    private static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    private static string ComputeCodeChallenge(string codeVerifier) =>
        Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void SaveRefreshToken(string token)
    {
        var dir = Path.GetDirectoryName(TokenFilePath)!;
        Directory.CreateDirectory(dir);
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(TokenFilePath, protectedBytes);
    }

    private static string? LoadRefreshToken()
    {
        if (!File.Exists(TokenFilePath)) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(TokenFilePath), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException) { return null; }
    }

    private static async Task WriteHtmlAsync(HttpListenerResponse response, string html)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.OutputStream.Close();
    }

    private const string PageStyle = "font-family:Segoe UI,sans-serif;background:#080b11;color:#f0f0f0;" +
        "display:flex;align-items:center;justify-content:center;height:100vh;margin:0;font-size:16px;";

    private const string SuccessHtml =
        $"<html><body style=\"{PageStyle}\">Connected to YouTube. You can close this window.</body></html>";

    private static string ErrorHtml(string message) =>
        $"<html><body style=\"{PageStyle}\">YouTube sign-in failed: {WebUtility.HtmlEncode(message)}</body></html>";
}
