using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using StreamFlow.App.Services.AI.Contracts;
using StreamFlow.Core.Data.Ai;

namespace StreamFlow.App.Services.AI.Providers;

/// <summary>Automatic1111 (stable-diffusion-webui) — connects to an already-running local server
/// (default http://localhost:7860, started with --api). Simple prompt-in/image-out REST, unlike
/// ComfyUI's node-graph model: /sdapi/v1/txt2img takes a prompt directly and returns base64
/// images, and /sdapi/v1/sd-models doubles as both the model list and a cheap reachability
/// check.</summary>
public sealed class Automatic1111ProviderClient : IImageGenerationClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public AiProviderKind Kind => AiProviderKind.Automatic1111;

    public Automatic1111ProviderClient(string baseUrl, HttpClient? httpClient = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    // ── Test Connection (model list) ────────────────────────────────────────

    public HttpRequestMessage BuildModelsRequest() => new(HttpMethod.Get, $"{_baseUrl}/sdapi/v1/sd-models");

    public static AiConnectionTestResult ParseModelsResponse(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            var models = (root as JsonArray)?
                .Select(m => m?["model_name"]?.GetValue<string>() ?? m?["title"]?.GetValue<string>())
                .Where(name => name is not null)
                .Select(name => name!)
                .ToList() ?? [];
            return AiConnectionTestResult.Ok(models);
        }
        catch (JsonException)
        {
            return AiConnectionTestResult.Failed("Unexpected response from Automatic1111.");
        }
    }

    public async Task<AiConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.SendAsync(BuildModelsRequest(), ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return resp.IsSuccessStatusCode
                ? ParseModelsResponse(body)
                : AiConnectionTestResult.Failed($"Automatic1111 returned {(int)resp.StatusCode}. Was it started with --api?");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return AiConnectionTestResult.Failed($"Couldn't reach Automatic1111 at {_baseUrl} — is it running with --api? ({ex.Message})");
        }
    }

    // ── Image generation ─────────────────────────────────────────────────────

    public HttpRequestMessage BuildTxt2ImgRequest(ImageGenerationRequest request)
    {
        var body = new JsonObject
        {
            ["prompt"] = request.Prompt,
            ["width"] = request.Width,
            ["height"] = request.Height,
        };
        if (!string.IsNullOrEmpty(request.NegativePrompt)) body["negative_prompt"] = request.NegativePrompt;
        if (request.Steps is int steps) body["steps"] = steps;
        if (request.Seed is int seed) body["seed"] = seed;

        return new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/sdapi/v1/txt2img")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }

    public static ImageGenerationResult ParseTxt2ImgResponse(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            if (root?["error"] is JsonNode err)
                return ImageGenerationResult.Failed(err["errors"]?.GetValue<string>() ?? "Automatic1111 returned an error.");

            var images = (root?["images"] as JsonArray)?
                .Select(i => i?.GetValue<string>())
                .Where(b64 => b64 is not null)
                .Select(b64 => Convert.FromBase64String(b64!))
                .ToList() ?? [];

            return images.Count > 0
                ? ImageGenerationResult.Ok(images)
                : ImageGenerationResult.Failed("Automatic1111 response didn't contain any image data.");
        }
        catch (JsonException)
        {
            return ImageGenerationResult.Failed("Unexpected response from Automatic1111.");
        }
    }

    public async Task<ImageGenerationResult> GenerateImageAsync(ImageGenerationRequest request, CancellationToken ct = default)
    {
        try
        {
            // Image generation can genuinely take much longer than the connection-test timeout
            // (tens of seconds for a large batch/resolution on modest hardware).
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(5));

            using var resp = await _http.SendAsync(BuildTxt2ImgRequest(request), cts.Token);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return resp.IsSuccessStatusCode
                ? ParseTxt2ImgResponse(body)
                : ImageGenerationResult.Failed($"Automatic1111 returned {(int)resp.StatusCode}: {body}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return ImageGenerationResult.Failed($"Couldn't reach Automatic1111 at {_baseUrl}: {ex.Message}");
        }
    }
}
