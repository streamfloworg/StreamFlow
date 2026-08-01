using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;

using StreamFlow.App.Services.AI.Contracts;
using StreamFlow.Core.Data.Ai;

namespace StreamFlow.App.Services.AI.Providers;

/// <summary>ComfyUI — connects to an already-running local server (default
/// http://localhost:8188). Unlike Automatic1111, there's no simple generate-image endpoint:
/// ComfyUI executes a node-graph "workflow" submitted to /prompt, then the result is retrieved by
/// polling /history/{id} and fetching each output image via /view. See ComfyUiWorkflowTemplate
/// for how a plain prompt/seed/steps/size request gets patched into the bundled (or a
/// user-supplied) workflow graph.</summary>
public sealed class ComfyUiProviderClient : IImageGenerationClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _clientId = Guid.NewGuid().ToString("N");
    private readonly Func<string> _loadWorkflowJson;

    public AiProviderKind Kind => AiProviderKind.ComfyUi;

    public ComfyUiProviderClient(string baseUrl, HttpClient? httpClient = null, Func<string>? loadWorkflowJson = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _loadWorkflowJson = loadWorkflowJson ?? ComfyUiWorkflowTemplate.LoadBundledDefault;
    }

    // ── Test Connection — reachability only, never submits a real workflow ────

    public HttpRequestMessage BuildSystemStatsRequest() => new(HttpMethod.Get, $"{_baseUrl}/system_stats");

    public async Task<AiConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.SendAsync(BuildSystemStatsRequest(), ct);
            if (!resp.IsSuccessStatusCode)
                return AiConnectionTestResult.Failed($"ComfyUI returned {(int)resp.StatusCode}.");

            // Best-effort checkpoint list — /object_info/CheckpointLoaderSimple isn't essential
            // for reachability, so a failure here doesn't fail the connection test itself.
            var models = await TryListCheckpointsAsync(ct);
            return AiConnectionTestResult.Ok(models);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return AiConnectionTestResult.Failed($"Couldn't reach ComfyUI at {_baseUrl} — is it running? ({ex.Message})");
        }
    }

    private async Task<IReadOnlyList<string>> TryListCheckpointsAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync($"{_baseUrl}/object_info/CheckpointLoaderSimple", ct);
            if (!resp.IsSuccessStatusCode) return [];
            var json = await resp.Content.ReadAsStringAsync(ct);
            var root = JsonNode.Parse(json);
            var names = root?["CheckpointLoaderSimple"]?["input"]?["required"]?["ckpt_name"]?[0] as JsonArray;
            return names?.Select(n => n?.GetValue<string>()).Where(n => n is not null).Select(n => n!).ToList() ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return [];
        }
    }

    // ── Image generation ─────────────────────────────────────────────────────

    public async Task<ImageGenerationResult> GenerateImageAsync(ImageGenerationRequest request, CancellationToken ct = default)
    {
        JsonObject graph;
        try
        {
            graph = ComfyUiWorkflowTemplate.Patch(
                _loadWorkflowJson(), request.Prompt, request.NegativePrompt,
                request.Seed, request.Steps, request.Width, request.Height, request.Model);
        }
        catch (InvalidOperationException ex)
        {
            return ImageGenerationResult.Failed($"ComfyUI workflow template problem: {ex.Message}");
        }

        var saveImageNodeIds = ComfyUiWorkflowTemplate.FindSaveImageNodeIds(graph);
        if (saveImageNodeIds.Count == 0)
            return ImageGenerationResult.Failed("ComfyUI workflow template has no SaveImage node — nothing to retrieve.");

        try
        {
            var promptId = await SubmitPromptAsync(graph, ct);
            if (promptId is null)
                return ImageGenerationResult.Failed("ComfyUI didn't return a prompt id for the submitted workflow.");

            var outputs = await PollForOutputsAsync(promptId, ct);
            if (outputs is null)
                return ImageGenerationResult.Failed("Timed out waiting for ComfyUI to finish generating.");

            var images = new List<byte[]>();
            foreach (var nodeId in saveImageNodeIds)
            {
                if (outputs[nodeId]?["images"] is not JsonArray imageRefs) continue;
                foreach (var imgRef in imageRefs)
                {
                    var filename = imgRef?["filename"]?.GetValue<string>();
                    var subfolder = imgRef?["subfolder"]?.GetValue<string>() ?? "";
                    var type = imgRef?["type"]?.GetValue<string>() ?? "output";
                    if (filename is null) continue;

                    var bytes = await FetchImageAsync(filename, subfolder, type, ct);
                    if (bytes is not null) images.Add(bytes);
                }
            }

            return images.Count > 0
                ? ImageGenerationResult.Ok(images)
                : ImageGenerationResult.Failed("ComfyUI finished but produced no retrievable images.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return ImageGenerationResult.Failed($"Couldn't reach ComfyUI at {_baseUrl}: {ex.Message}");
        }
    }

    private async Task<string?> SubmitPromptAsync(JsonObject graph, CancellationToken ct)
    {
        var body = new JsonObject { ["prompt"] = graph, ["client_id"] = _clientId };
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/prompt")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        using var resp = await _http.SendAsync(req, ct);
        var responseBody = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"ComfyUI returned {(int)resp.StatusCode} submitting the workflow: {responseBody}");

        return JsonNode.Parse(responseBody)?["prompt_id"]?.GetValue<string>();
    }

    /// <summary>Polls /history/{promptId} until ComfyUI reports this prompt's outputs, up to a
    /// 5-minute budget — generation time varies wildly with hardware/model/resolution, unlike a
    /// cheap connection test.</summary>
    private async Task<JsonObject?> PollForOutputsAsync(string promptId, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMinutes(5);
        while (DateTime.UtcNow < deadline)
        {
            using var resp = await _http.GetAsync($"{_baseUrl}/history/{promptId}", ct);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(ct);
                var outputs = JsonNode.Parse(json)?[promptId]?["outputs"] as JsonObject;
                if (outputs is not null) return outputs;
            }
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
        return null;
    }

    private async Task<byte[]?> FetchImageAsync(string filename, string subfolder, string type, CancellationToken ct)
    {
        var query = HttpUtility.ParseQueryString("");
        query["filename"] = filename;
        query["subfolder"] = subfolder;
        query["type"] = type;
        using var resp = await _http.GetAsync($"{_baseUrl}/view?{query}", ct);
        return resp.IsSuccessStatusCode ? await resp.Content.ReadAsByteArrayAsync(ct) : null;
    }
}
