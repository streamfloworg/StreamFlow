using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

using Microsoft.Extensions.Configuration;

using StreamFlow.Core.Helpers;

using Microsoft.Extensions.Logging;

namespace StreamFlow.App.Services;

public sealed record TwitchAuthResult(string AccessToken, string Username, string UserId);

/// <summary>
/// Twitch sign-in via the Implicit Grant flow (no client secret — Twitch's recommended
/// approach for native/public clients, since a secret embedded in a distributed desktop
/// binary can't actually stay secret). Catches the redirect on a local loopback listener.
/// Twitch never exposes stream keys via API, so this is only used to identify the connected
/// account; the stream key itself is still entered manually.
/// </summary>
public sealed class TwitchAuthService
{
    private const string RedirectUri = "http://localhost:3990/callback";
    private readonly string _clientId;
    private readonly HttpClient _http = new();
    private readonly ILogger<TwitchAuthService> _logger;

    private static string TokenFilePath => Path.Combine(AppDataPaths.RootFolder, "twitch_token.dat");

    public TwitchAuthService(IConfiguration config, ILogger<TwitchAuthService> logger)
    {
        _clientId = config["Twitch:ClientId"] ?? "";
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_clientId);

    public string? GetAccessToken() => LoadToken();

    /// <summary>Tries to resume a previous session from the saved token, if any.</summary>
    public async Task<TwitchAuthResult?> TryRestoreAsync(CancellationToken ct = default)
    {
        var token = LoadToken();
        return token is null ? null : await ValidateAsync(token, ct);
    }

    /// <summary>Runs the full sign-in flow: opens the system browser, waits for the redirect, validates the token.</summary>
    public async Task<TwitchAuthResult?> ConnectAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) return null;

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

        using var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:3990/");
        listener.Start();

        var authorizeUrl =
            "https://id.twitch.tv/oauth2/authorize" +
            $"?client_id={Uri.EscapeDataString(_clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
            "&response_type=token" +
            "&scope=channel:manage:broadcast+channel:read:stream_key" +
            $"&state={state}";

        Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true });

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync().WaitAsync(ct);
                var request = context.Request;
                var response = context.Response;

                if (request.Url?.AbsolutePath != "/callback")
                {
                    response.StatusCode = 404;
                    response.Close();
                    continue;
                }

                if (string.IsNullOrEmpty(request.Url.Query))
                {
                    // The token arrives in the URL fragment, which browsers never send to the
                    // server. Relay it back to ourselves as a query string via a tiny JS hop.
                    await WriteHtmlAsync(response, RelayHtml);
                    continue;
                }

                var query = HttpUtility.ParseQueryString(request.Url.Query);
                var error = query["error"];
                if (error is not null)
                {
                    await WriteHtmlAsync(response, ErrorHtml(query["error_description"] ?? error));
                    return null;
                }

                var accessToken = query["access_token"];
                if (query["state"] != state || string.IsNullOrEmpty(accessToken))
                {
                    await WriteHtmlAsync(response, ErrorHtml("Login response didn't match the request. Please try again."));
                    return null;
                }

                var result = await ValidateAsync(accessToken, ct);
                await WriteHtmlAsync(response, result is not null ? SuccessHtml : ErrorHtml("Couldn't verify the Twitch login."));

                if (result is not null)
                    SaveToken(accessToken);

                return result;
            }
        }
        finally
        {
            listener.Stop();
        }

        return null;
    }

    public void Disconnect()
    {
        try { File.Delete(TokenFilePath); } catch (IOException) { }
    }

    private async Task<TwitchAuthResult?> ValidateAsync(string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
        req.Headers.TryAddWithoutValidation("Authorization", $"OAuth {token}");

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));
        var login = doc.RootElement.TryGetProperty("login", out var loginProp) ? loginProp.GetString() : null;
        var userId = doc.RootElement.TryGetProperty("user_id", out var idProp) ? idProp.GetString() : null;
        return login is null || userId is null ? null : new TwitchAuthResult(token, login, userId);
    }

    private static void SaveToken(string token)
    {
        var dir = Path.GetDirectoryName(TokenFilePath)!;
        Directory.CreateDirectory(dir);
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(TokenFilePath, protectedBytes);
    }

    private static string? LoadToken()
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

    private const string RelayHtml =
        $"<html><body style=\"{PageStyle}\"><script>window.location.replace('/callback?' + window.location.hash.substring(1));</script>Connecting…</body></html>";

    private const string SuccessHtml =
        $"<html><body style=\"{PageStyle}\">Connected to Twitch. You can close this window.</body></html>";

    private static string ErrorHtml(string message) =>
        $"<html><body style=\"{PageStyle}\">Twitch sign-in failed: {WebUtility.HtmlEncode(message)}</body></html>";

    /// <summary>Updates the Twitch stream's title and category/game name.</summary>
    public async Task<bool> UpdateStreamInfoAsync(string token, string userId, string title, string gameName, CancellationToken ct = default)
    {
        try
        {
            string? gameId = null;
            if (!string.IsNullOrWhiteSpace(gameName))
            {
                // Search for the game category to get the category ID
                var searchUrl = $"https://api.twitch.tv/helix/search/categories?query={Uri.EscapeDataString(gameName)}";
                using var searchReq = new HttpRequestMessage(HttpMethod.Get, searchUrl);
                searchReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                searchReq.Headers.Add("Client-Id", _clientId);

                using var searchResp = await _http.SendAsync(searchReq, ct);
                if (searchResp.IsSuccessStatusCode)
                {
                    using var searchDoc = JsonDocument.Parse(await searchResp.Content.ReadAsStreamAsync(ct));
                    if (searchDoc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
                    {
                        // Match game by exact name or use the first search result
                        gameId = data[0].GetProperty("id").GetString();
                        for (int i = 0; i < data.GetArrayLength(); i++)
                        {
                            var name = data[i].GetProperty("name").GetString();
                            if (string.Equals(name, gameName, StringComparison.OrdinalIgnoreCase))
                            {
                                gameId = data[i].GetProperty("id").GetString();
                                break;
                            }
                        }
                    }
                }
            }

            // Update channel information
            var updateUrl = $"https://api.twitch.tv/helix/channels?broadcaster_id={Uri.EscapeDataString(userId)}";
            var bodyObj = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(title))
                bodyObj["title"] = title;
            if (!string.IsNullOrWhiteSpace(gameId))
                bodyObj["game_id"] = gameId;

            if (bodyObj.Count == 0) return true;

            var jsonBody = JsonSerializer.Serialize(bodyObj);
            using var patchReq = new HttpRequestMessage(HttpMethod.Patch, updateUrl)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };
            patchReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            patchReq.Headers.Add("Client-Id", _clientId);

            using var patchResp = await _http.SendAsync(patchReq, ct);
            return patchResp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update Twitch channel info");
            return false;
        }
    }

    /// <summary>Fetches the Twitch channel's stream key via Helix API.</summary>
    public async Task<string?> TryFetchStreamKeyAsync(string token, string userId, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://api.twitch.tv/helix/streams/key?broadcaster_id={Uri.EscapeDataString(userId)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("Client-Id", _clientId);

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Twitch streams/key API failed: {Status}", resp.StatusCode);
                return null;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));
            if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
            {
                return data[0].GetProperty("stream_key").GetString();
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Twitch stream key");
            return null;
        }
    }

    public async Task<List<string>> SearchCategoriesAsync(string token, string query, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
                return [];

            var searchUrl = $"https://api.twitch.tv/helix/search/categories?query={Uri.EscapeDataString(query)}";
            using var searchReq = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            searchReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            searchReq.Headers.Add("Client-Id", _clientId);

            using var searchResp = await _http.SendAsync(searchReq, ct);
            if (searchResp.IsSuccessStatusCode)
            {
                using var searchDoc = JsonDocument.Parse(await searchResp.Content.ReadAsStreamAsync(ct));
                if (searchDoc.RootElement.TryGetProperty("data", out var data))
                {
                    var list = new List<string>();
                    for (int i = 0; i < data.GetArrayLength(); i++)
                    {
                        var name = data[i].GetProperty("name").GetString();
                        if (name is not null)
                            list.Add(name);
                    }
                    return list;
                }
            }
        }
        catch
        {
            // Ignore API failures and return empty list
        }
        return [];
    }

    /// <summary>Fetches the active Twitch channel's title and category/game name.</summary>
    public async Task<(string Title, string GameName)?> TryFetchChannelInfoAsync(string token, string userId, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://api.twitch.tv/helix/channels?broadcaster_id={Uri.EscapeDataString(userId)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("Client-Id", _clientId);

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Twitch channels API failed: {Status}", resp.StatusCode);
                return null;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));
            if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
            {
                var item = data[0];
                var title = item.GetProperty("title").GetString() ?? "";
                var gameName = item.GetProperty("game_name").GetString() ?? "";
                return (title, gameName);
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Twitch channel info");
            return null;
        }
    }

    public async Task<int?> TryFetchViewerCountAsync(string token, string userId, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://api.twitch.tv/helix/streams?user_id={Uri.EscapeDataString(userId)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("Client-Id", _clientId);

            using var resp = await _http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));
                if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
                {
                    if (data[0].TryGetProperty("viewer_count", out var viewersProp))
                    {
                        return viewersProp.GetInt32();
                    }
                }
                return 0; // Stream is offline
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Twitch viewer count");
        }
        return null;
    }
}
