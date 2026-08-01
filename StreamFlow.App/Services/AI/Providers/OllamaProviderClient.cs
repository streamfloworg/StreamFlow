using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

using StreamFlow.App.Services.AI.Contracts;
using StreamFlow.Core.Data.Ai;

namespace StreamFlow.App.Services.AI.Providers;

/// <summary>Ollama — connects to an already-running local Ollama server (default
/// http://localhost:11434). Model listing uses Ollama's own native /api/tags rather than its
/// OpenAI-compat /v1/models, since /api/tags is the more established/reliable endpoint for this.
/// Text generation reuses the shared OpenAI-compatible /v1/chat/completions from the base class.</summary>
public sealed class OllamaProviderClient : OpenAiCompatibleLocalClientBase
{
    public override AiProviderKind Kind => AiProviderKind.Ollama;

    public OllamaProviderClient(string baseUrl, string? apiKey = null, HttpClient? httpClient = null)
        : base(baseUrl, apiKey, httpClient)
    {
    }

    public HttpRequestMessage BuildTagsRequest() => new(HttpMethod.Get, $"{BaseUrl}/api/tags");

    public static AiConnectionTestResult ParseTagsResponse(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            var models = (root?["models"] as JsonArray)?
                .Select(m => m?["name"]?.GetValue<string>())
                .Where(name => name is not null)
                .Select(name => name!)
                .ToList() ?? [];
            return AiConnectionTestResult.Ok(models);
        }
        catch (JsonException)
        {
            return AiConnectionTestResult.Failed("Unexpected response from Ollama.");
        }
    }

    public override async Task<AiConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await Http.SendAsync(BuildTagsRequest(), ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return resp.IsSuccessStatusCode
                ? ParseTagsResponse(body)
                : AiConnectionTestResult.Failed($"Ollama returned {(int)resp.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return AiConnectionTestResult.Failed($"Couldn't reach Ollama at {BaseUrl} — is it running? ({ex.Message})");
        }
    }
}
