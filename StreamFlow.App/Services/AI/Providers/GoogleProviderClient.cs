using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using StreamFlow.App.Services.AI.Contracts;
using StreamFlow.Core.Data.Ai;

namespace StreamFlow.App.Services.AI.Providers;

/// <summary>Google Gemini client against the Generative Language API (generativelanguage.googleapis.com,
/// plain API-key auth) rather than Vertex AI — Vertex requires a GCP project + service-account/OAuth
/// setup, which is much more friction than the "AI Studio" API-key model every other cloud provider
/// here uses. Hand-rolled REST, matching this app's existing convention (YouTubeAuthService already
/// hand-rolls Google OAuth+API calls rather than using Google.Apis.*) rather than pulling in the
/// heavy Vertex AI/gRPC SDK for what's a couple of simple HTTP calls.</summary>
public sealed class GoogleProviderClient : ITextGenerationClient, IImageGenerationClient
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";

    private readonly HttpClient _http;
    private readonly string _apiKey;

    public AiProviderKind Kind => AiProviderKind.Google;

    public GoogleProviderClient(string apiKey, HttpClient? httpClient = null)
    {
        _apiKey = apiKey;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    // ── Test Connection (models list) ──────────────────────────────────────────

    public static HttpRequestMessage BuildModelsRequest(string apiKey) =>
        new(HttpMethod.Get, $"{BaseUrl}/models?key={Uri.EscapeDataString(apiKey)}");

    public static AiConnectionTestResult ParseModelsResponse(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            if (root?["error"] is JsonNode err)
                return AiConnectionTestResult.Failed(err["message"]?.GetValue<string>() ?? "Google returned an error.");

            // Model names come back as "models/gemini-1.5-pro" — strip the "models/" prefix so
            // callers see the bare id, matching what generateContent's :model segment expects.
            var models = (root?["models"] as JsonArray)?
                .Select(m => m?["name"]?.GetValue<string>())
                .Where(name => name is not null)
                .Select(name => name!.StartsWith("models/") ? name["models/".Length..] : name)
                .ToList() ?? [];
            return AiConnectionTestResult.Ok(models);
        }
        catch (JsonException)
        {
            return AiConnectionTestResult.Failed("Unexpected response from Google.");
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
                : AiConnectionTestResult.Failed(ExtractErrorMessage(body) ?? $"Google returned {(int)resp.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return AiConnectionTestResult.Failed($"Couldn't reach Google: {ex.Message}");
        }
    }

    // ── Text generation ──────────────────────────────────────────────────────

    public static HttpRequestMessage BuildGenerateContentRequest(string apiKey, TextGenerationRequest request)
    {
        var body = new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = request.Prompt } } },
            },
        };
        if (!string.IsNullOrEmpty(request.SystemPrompt))
            body["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = request.SystemPrompt } } };
        if (request.Temperature is double temp || request.MaxTokens is int)
        {
            var genConfig = new JsonObject();
            if (request.Temperature is double t) genConfig["temperature"] = t;
            if (request.MaxTokens is int maxTokens) genConfig["maxOutputTokens"] = maxTokens;
            body["generationConfig"] = genConfig;
        }

        return new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/models/{request.Model}:generateContent?key={Uri.EscapeDataString(apiKey)}")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }

    public static TextGenerationResult ParseGenerateContentResponse(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            if (root?["error"] is JsonNode err)
                return TextGenerationResult.Failed(err["message"]?.GetValue<string>() ?? "Google returned an error.");

            var text = (root?["candidates"]?[0]?["content"]?["parts"] as JsonArray)?
                .Select(p => p?["text"]?.GetValue<string>())
                .FirstOrDefault(t => t is not null);

            return text is not null
                ? TextGenerationResult.Ok(text)
                : TextGenerationResult.Failed("Google response didn't contain any completion text.");
        }
        catch (JsonException)
        {
            return TextGenerationResult.Failed("Unexpected response from Google.");
        }
    }

    public async Task<TextGenerationResult> GenerateTextAsync(TextGenerationRequest request, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.SendAsync(BuildGenerateContentRequest(_apiKey, request), ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return resp.IsSuccessStatusCode
                ? ParseGenerateContentResponse(body)
                : TextGenerationResult.Failed(ExtractErrorMessage(body) ?? $"Google returned {(int)resp.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return TextGenerationResult.Failed($"Couldn't reach Google: {ex.Message}");
        }
    }

    // ── Image generation ─────────────────────────────────────────────────────
    // Uses the same generateContent endpoint against an image-capable Gemini model, which returns
    // generated images as inline base64 data (inlineData.data) among the response parts, rather
    // than Imagen's separate :predict endpoint — simpler (one endpoint shape for both modalities)
    // and matches how Gemini's multimodal models expose image output today. Verify the exact
    // current image-capable model id against Google's docs at implementation time.

    public static HttpRequestMessage BuildImageRequest(string apiKey, ImageGenerationRequest request)
    {
        var body = new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = request.Prompt } } },
            },
        };
        var model = string.IsNullOrEmpty(request.Model) ? "gemini-2.0-flash-exp" : request.Model;

        return new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/models/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }

    public static ImageGenerationResult ParseImageResponse(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            if (root?["error"] is JsonNode err)
                return ImageGenerationResult.Failed(err["message"]?.GetValue<string>() ?? "Google returned an error.");

            var images = (root?["candidates"]?[0]?["content"]?["parts"] as JsonArray)?
                .Select(p => p?["inlineData"]?["data"]?.GetValue<string>())
                .Where(b64 => b64 is not null)
                .Select(b64 => Convert.FromBase64String(b64!))
                .ToList() ?? [];

            return images.Count > 0
                ? ImageGenerationResult.Ok(images)
                : ImageGenerationResult.Failed("Google response didn't contain any image data.");
        }
        catch (JsonException)
        {
            return ImageGenerationResult.Failed("Unexpected response from Google.");
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
                : ImageGenerationResult.Failed(ExtractErrorMessage(body) ?? $"Google returned {(int)resp.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ImageGenerationResult.Failed($"Couldn't reach Google: {ex.Message}");
        }
    }

    internal static string? ExtractErrorMessage(string json)
    {
        try { return JsonNode.Parse(json)?["error"]?["message"]?.GetValue<string>(); }
        catch (JsonException) { return null; }
    }
}
