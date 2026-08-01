using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using StreamFlow.App.Services.AI.Contracts;
using StreamFlow.Core.Data.Ai;

namespace StreamFlow.App.Services.AI.Providers;

/// <summary>Anthropic Messages API client — text generation only (Anthropic has no image
/// generation API; see AiProviderCatalog). No official .NET SDK exists for Anthropic, and the
/// surface needed here is small and stable, so this hand-rolls the REST calls rather than taking
/// a community SDK dependency — consistent with every other provider adapter in this app.</summary>
public sealed class AnthropicProviderClient : ITextGenerationClient
{
    private const string BaseUrl = "https://api.anthropic.com/v1";
    private const string AnthropicVersion = "2023-06-01";

    private readonly HttpClient _http;
    private readonly string _apiKey;

    public AiProviderKind Kind => AiProviderKind.Anthropic;

    public AnthropicProviderClient(string apiKey, HttpClient? httpClient = null)
    {
        _apiKey = apiKey;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    private static void AddAuthHeaders(HttpRequestMessage req, string apiKey)
    {
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", AnthropicVersion);
    }

    // ── Test Connection (models list) ──────────────────────────────────────────

    public static HttpRequestMessage BuildModelsRequest(string apiKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models");
        AddAuthHeaders(req, apiKey);
        return req;
    }

    public static AiConnectionTestResult ParseModelsResponse(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            if (root?["type"]?.GetValue<string>() == "error")
                return AiConnectionTestResult.Failed(root["error"]?["message"]?.GetValue<string>() ?? "Anthropic returned an error.");

            var models = (root?["data"] as JsonArray)?
                .Select(m => m?["id"]?.GetValue<string>())
                .Where(id => id is not null)
                .Select(id => id!)
                .ToList() ?? [];
            return AiConnectionTestResult.Ok(models);
        }
        catch (JsonException)
        {
            return AiConnectionTestResult.Failed("Unexpected response from Anthropic.");
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
                : AiConnectionTestResult.Failed(ExtractErrorMessage(body) ?? $"Anthropic returned {(int)resp.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return AiConnectionTestResult.Failed($"Couldn't reach Anthropic: {ex.Message}");
        }
    }

    // ── Text generation ──────────────────────────────────────────────────────

    public static HttpRequestMessage BuildMessagesRequest(string apiKey, TextGenerationRequest request)
    {
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["max_tokens"] = request.MaxTokens ?? 1024,
            ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = request.Prompt } },
        };
        if (!string.IsNullOrEmpty(request.SystemPrompt)) body["system"] = request.SystemPrompt;
        if (request.Temperature is double temp) body["temperature"] = temp;

        var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/messages")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        AddAuthHeaders(req, apiKey);
        return req;
    }

    public static TextGenerationResult ParseMessagesResponse(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            if (root?["type"]?.GetValue<string>() == "error")
                return TextGenerationResult.Failed(root["error"]?["message"]?.GetValue<string>() ?? "Anthropic returned an error.");

            // content is an array of blocks; only the "text" blocks matter for a plain completion.
            var text = (root?["content"] as JsonArray)?
                .Where(b => b?["type"]?.GetValue<string>() == "text")
                .Select(b => b?["text"]?.GetValue<string>() ?? "")
                .Aggregate("", (acc, t) => acc + t);

            return !string.IsNullOrEmpty(text)
                ? TextGenerationResult.Ok(text)
                : TextGenerationResult.Failed("Anthropic response didn't contain any completion text.");
        }
        catch (JsonException)
        {
            return TextGenerationResult.Failed("Unexpected response from Anthropic.");
        }
    }

    public async Task<TextGenerationResult> GenerateTextAsync(TextGenerationRequest request, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.SendAsync(BuildMessagesRequest(_apiKey, request), ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return resp.IsSuccessStatusCode
                ? ParseMessagesResponse(body)
                : TextGenerationResult.Failed(ExtractErrorMessage(body) ?? $"Anthropic returned {(int)resp.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return TextGenerationResult.Failed($"Couldn't reach Anthropic: {ex.Message}");
        }
    }

    internal static string? ExtractErrorMessage(string json)
    {
        try { return JsonNode.Parse(json)?["error"]?["message"]?.GetValue<string>(); }
        catch (JsonException) { return null; }
    }
}
