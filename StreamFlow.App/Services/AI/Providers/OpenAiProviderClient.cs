using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using StreamFlow.App.Services.AI.Contracts;
using StreamFlow.Core.Data.Ai;

namespace StreamFlow.App.Services.AI.Providers;

/// <summary>OpenAI REST client — chat completions, image generation, and model listing. Hand-rolled
/// HttpClient + System.Text.Json rather than the official OpenAI SDK, matching this codebase's
/// existing convention (TwitchAuthService/YouTubeAuthService both hand-roll REST too) and keeping
/// the request-build/response-parse seam this class needs for unit testing without hitting the
/// official SDK's own abstractions.</summary>
public sealed class OpenAiProviderClient : ITextGenerationClient, IImageGenerationClient
{
    private const string BaseUrl = "https://api.openai.com/v1";

    private readonly HttpClient _http;
    private readonly string _apiKey;

    public AiProviderKind Kind => AiProviderKind.OpenAI;

    public OpenAiProviderClient(string apiKey, HttpClient? httpClient = null)
    {
        _apiKey = apiKey;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    // ── Test Connection (models list) ──────────────────────────────────────────

    public static HttpRequestMessage BuildModelsRequest(string apiKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return req;
    }

    public static AiConnectionTestResult ParseModelsResponse(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            if (root?["error"] is JsonNode err)
                return AiConnectionTestResult.Failed(err["message"]?.GetValue<string>() ?? "OpenAI returned an error.");

            var models = (root?["data"] as JsonArray)?
                .Select(m => m?["id"]?.GetValue<string>())
                .Where(id => id is not null)
                .Select(id => id!)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];
            return AiConnectionTestResult.Ok(models);
        }
        catch (JsonException)
        {
            return AiConnectionTestResult.Failed("Unexpected response from OpenAI.");
        }
    }

    public async Task<AiConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.SendAsync(BuildModelsRequest(_apiKey), ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return resp.IsSuccessStatusCode
                ? ParseModelsResponse(body)
                : AiConnectionTestResult.Failed(ExtractErrorMessage(body) ?? $"OpenAI returned {(int)resp.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return AiConnectionTestResult.Failed($"Couldn't reach OpenAI: {ex.Message}");
        }
    }

    // ── Text generation ──────────────────────────────────────────────────────

    public static HttpRequestMessage BuildChatRequest(string apiKey, TextGenerationRequest request)
    {
        var messages = new JsonArray();
        if (!string.IsNullOrEmpty(request.SystemPrompt))
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt });
        messages.Add(new JsonObject { ["role"] = "user", ["content"] = request.Prompt });

        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = messages,
        };
        if (request.Temperature is double temp) body["temperature"] = temp;
        if (request.MaxTokens is int maxTokens) body["max_tokens"] = maxTokens;

        var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return req;
    }

    public static TextGenerationResult ParseChatResponse(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            if (root?["error"] is JsonNode err)
                return TextGenerationResult.Failed(err["message"]?.GetValue<string>() ?? "OpenAI returned an error.");

            var text = root?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
            return text is not null
                ? TextGenerationResult.Ok(text)
                : TextGenerationResult.Failed("OpenAI response didn't contain any completion text.");
        }
        catch (JsonException)
        {
            return TextGenerationResult.Failed("Unexpected response from OpenAI.");
        }
    }

    public async Task<TextGenerationResult> GenerateTextAsync(TextGenerationRequest request, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.SendAsync(BuildChatRequest(_apiKey, request), ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return resp.IsSuccessStatusCode
                ? ParseChatResponse(body)
                : TextGenerationResult.Failed(ExtractErrorMessage(body) ?? $"OpenAI returned {(int)resp.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return TextGenerationResult.Failed($"Couldn't reach OpenAI: {ex.Message}");
        }
    }

    // ── Image generation ─────────────────────────────────────────────────────

    public static HttpRequestMessage BuildImageRequest(string apiKey, ImageGenerationRequest request)
    {
        var body = new JsonObject
        {
            ["model"] = request.Model ?? "gpt-image-1",
            ["prompt"] = request.Prompt,
            ["n"] = 1,
            ["size"] = $"{request.Width}x{request.Height}",
        };

        var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/images/generations")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return req;
    }

    /// <summary>Parses the images/generations response. OpenAI returns each image as either a
    /// base64 "b64_json" field or a temporary "url" — this only decodes the base64 form
    /// (gpt-image-1's default); a url-returning model would need an extra download round trip
    /// the caller doesn't currently do.</summary>
    public static ImageGenerationResult ParseImageResponse(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            if (root?["error"] is JsonNode err)
                return ImageGenerationResult.Failed(err["message"]?.GetValue<string>() ?? "OpenAI returned an error.");

            var images = (root?["data"] as JsonArray)?
                .Select(d => d?["b64_json"]?.GetValue<string>())
                .Where(b64 => b64 is not null)
                .Select(b64 => Convert.FromBase64String(b64!))
                .ToList() ?? [];

            return images.Count > 0
                ? ImageGenerationResult.Ok(images)
                : ImageGenerationResult.Failed("OpenAI response didn't contain any image data (a URL-returning model isn't supported yet).");
        }
        catch (JsonException)
        {
            return ImageGenerationResult.Failed("Unexpected response from OpenAI.");
        }
    }

    public async Task<ImageGenerationResult> GenerateImageAsync(ImageGenerationRequest request, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.SendAsync(BuildImageRequest(_apiKey, request), ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return resp.IsSuccessStatusCode
                ? ParseImageResponse(body)
                : ImageGenerationResult.Failed(ExtractErrorMessage(body) ?? $"OpenAI returned {(int)resp.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ImageGenerationResult.Failed($"Couldn't reach OpenAI: {ex.Message}");
        }
    }

    internal static string? ExtractErrorMessage(string json)
    {
        try { return JsonNode.Parse(json)?["error"]?["message"]?.GetValue<string>(); }
        catch (JsonException) { return null; }
    }
}
