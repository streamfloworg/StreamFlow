using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

using StreamFlow.App.Services.AI.Contracts;
using StreamFlow.Core.Data.Ai;

namespace StreamFlow.App.Services.AI.Providers;

/// <summary>LM Studio — connects to an already-running local LM Studio server (default
/// http://localhost:1234). Model listing uses its OpenAI-shaped /v1/models endpoint. Text
/// generation reuses the shared OpenAI-compatible /v1/chat/completions from the base class.</summary>
public sealed class LmStudioProviderClient : OpenAiCompatibleLocalClientBase
{
    public override AiProviderKind Kind => AiProviderKind.LmStudio;

    public LmStudioProviderClient(string baseUrl, string? apiKey = null, HttpClient? httpClient = null)
        : base(baseUrl, apiKey, httpClient)
    {
    }

    public HttpRequestMessage BuildModelsRequest()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/v1/models");
        if (ApiKey is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        return req;
    }

    public static AiConnectionTestResult ParseModelsResponse(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            var models = (root?["data"] as JsonArray)?
                .Select(m => m?["id"]?.GetValue<string>())
                .Where(id => id is not null)
                .Select(id => id!)
                .ToList() ?? [];
            return AiConnectionTestResult.Ok(models);
        }
        catch (JsonException)
        {
            return AiConnectionTestResult.Failed("Unexpected response from LM Studio.");
        }
    }

    public override async Task<AiConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await Http.SendAsync(BuildModelsRequest(), ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return resp.IsSuccessStatusCode
                ? ParseModelsResponse(body)
                : AiConnectionTestResult.Failed($"LM Studio returned {(int)resp.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return AiConnectionTestResult.Failed($"Couldn't reach LM Studio at {BaseUrl} — is it running? ({ex.Message})");
        }
    }
}
