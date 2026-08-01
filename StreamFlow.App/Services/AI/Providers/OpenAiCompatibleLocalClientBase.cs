using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using StreamFlow.App.Services.AI.Contracts;
using StreamFlow.Core.Data.Ai;

namespace StreamFlow.App.Services.AI.Providers;

/// <summary>Shared base for local text providers that expose an OpenAI-compatible
/// /v1/chat/completions endpoint (Ollama and LM Studio both do) — the chat request/response shape
/// is identical between them, so it lives here once; each subclass only needs its own model-listing
/// endpoint, since Ollama's native /api/tags and LM Studio's OpenAI-shaped /v1/models differ.
/// No auth is required for a typical local install; ApiKey is optional, for tunneled/remote setups
/// that put the local server behind some auth of their own.</summary>
public abstract class OpenAiCompatibleLocalClientBase : ITextGenerationClient
{
    protected readonly HttpClient Http;
    protected readonly string BaseUrl;
    protected readonly string? ApiKey;

    public abstract AiProviderKind Kind { get; }

    protected OpenAiCompatibleLocalClientBase(string baseUrl, string? apiKey, HttpClient? httpClient)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
        Http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public abstract Task<AiConnectionTestResult> TestConnectionAsync(CancellationToken ct = default);

    // ── Text generation (shared OpenAI-shaped /v1/chat/completions) ────────────

    public HttpRequestMessage BuildChatRequest(TextGenerationRequest request)
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

        var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (ApiKey is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        return req;
    }

    public static TextGenerationResult ParseChatResponse(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            if (root?["error"] is JsonNode err)
            {
                var message = err is JsonObject ? err["message"]?.GetValue<string>() : err.GetValue<string>();
                return TextGenerationResult.Failed(message ?? "The local server returned an error.");
            }

            var text = root?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
            return text is not null
                ? TextGenerationResult.Ok(text)
                : TextGenerationResult.Failed("The local server's response didn't contain any completion text.");
        }
        catch (JsonException)
        {
            return TextGenerationResult.Failed("Unexpected response from the local server.");
        }
    }

    public async Task<TextGenerationResult> GenerateTextAsync(TextGenerationRequest request, CancellationToken ct = default)
    {
        try
        {
            using var resp = await Http.SendAsync(BuildChatRequest(request), ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return resp.IsSuccessStatusCode
                ? ParseChatResponse(body)
                : TextGenerationResult.Failed($"Local server returned {(int)resp.StatusCode}: {body}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return TextGenerationResult.Failed($"Couldn't reach {BaseUrl} — is the server running? ({ex.Message})");
        }
    }
}
