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

namespace StreamFlow.App.Services;

public sealed record TwitchAuthResult(string AccessToken, string Username);

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

    private static string TokenFilePath => Path.Combine(AppDataPaths.RootFolder, "twitch_token.dat");

    public TwitchAuthService(IConfiguration config)
    {
        _clientId = config["Twitch:ClientId"] ?? "";
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_clientId);

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
            "&scope=" +
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
        return login is null ? null : new TwitchAuthResult(token, login);
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
}
