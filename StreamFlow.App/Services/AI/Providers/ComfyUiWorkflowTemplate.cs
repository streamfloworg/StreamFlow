using System.IO;
using System.Text.Json.Nodes;

namespace StreamFlow.App.Services.AI.Providers;

/// <summary>ComfyUI has no simple generate-image endpoint — it executes a node-graph "workflow".
/// This loads a workflow template (the bundled default txt2img graph, or a user-supplied one —
/// see AiProviderProfileSettings.ComfyUiWorkflowTemplatePath) and patches the prompt/negative
/// prompt/seed/steps/size/checkpoint into the right node `inputs` by class_type, rather than
/// requiring the caller to understand ComfyUI's graph format at all.
///
/// Positive vs. negative CLIPTextEncode can't be told apart by class_type alone (a workflow
/// typically has two of them) — the KSampler node's own "positive"/"negative" links say which
/// node id is which, so this resolves those first and patches by id.</summary>
public static class ComfyUiWorkflowTemplate
{
    public static string LoadBundledDefault()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Services", "AI", "Providers", "Assets", "comfyui_default_txt2img_workflow.json");
        return File.ReadAllText(path);
    }

    /// <summary>Returns a patched copy of <paramref name="workflowJson"/> ready to POST to
    /// ComfyUI's /prompt endpoint (wrapped in the {"prompt": ...} envelope by the caller).
    /// Throws <see cref="InvalidOperationException"/> with a clear message if an expected node
    /// (KSampler, its positive/negative CLIPTextEncode targets, EmptyLatentImage) is missing from
    /// the template, rather than silently no-op'ing and submitting an unmodified workflow.</summary>
    public static JsonObject Patch(
        string workflowJson, string prompt, string? negativePrompt,
        int? seed, int? steps, int width, int height, string? checkpointName)
    {
        var graph = JsonNode.Parse(workflowJson) as JsonObject
            ?? throw new InvalidOperationException("ComfyUI workflow template isn't valid JSON.");

        var (sampler, samplerId) = FindNodeByClassType(graph, "KSampler")
            ?? throw new InvalidOperationException("ComfyUI workflow template has no KSampler node.");

        if (seed is int s) sampler["inputs"]!["seed"] = s;
        if (steps is int st) sampler["inputs"]!["steps"] = st;

        var positiveId = LinkedNodeId(sampler, "positive")
            ?? throw new InvalidOperationException($"KSampler node {samplerId} has no 'positive' link.");
        var negativeId = LinkedNodeId(sampler, "negative")
            ?? throw new InvalidOperationException($"KSampler node {samplerId} has no 'negative' link.");

        SetClipText(graph, positiveId, prompt);
        if (!string.IsNullOrEmpty(negativePrompt))
            SetClipText(graph, negativeId, negativePrompt);

        var (latent, _) = FindNodeByClassType(graph, "EmptyLatentImage")
            ?? throw new InvalidOperationException("ComfyUI workflow template has no EmptyLatentImage node.");
        latent["inputs"]!["width"] = width;
        latent["inputs"]!["height"] = height;

        if (!string.IsNullOrEmpty(checkpointName))
        {
            var checkpointNode = FindNodeByClassType(graph, "CheckpointLoaderSimple");
            if (checkpointNode is (JsonObject ckpt, _)) ckpt["inputs"]!["ckpt_name"] = checkpointName;
        }

        return graph;
    }

    /// <summary>Node id of every SaveImage node in the graph — the /history response's outputs
    /// are keyed by node id, so this is what tells the caller which output entries hold images.</summary>
    public static IReadOnlyList<string> FindSaveImageNodeIds(JsonObject graph) =>
        graph.Where(kv => (kv.Value as JsonObject)?["class_type"]?.GetValue<string>() == "SaveImage")
            .Select(kv => kv.Key)
            .ToList();

    private static (JsonObject Node, string Id)? FindNodeByClassType(JsonObject graph, string classType)
    {
        foreach (var (id, node) in graph)
        {
            if (node is JsonObject obj && obj["class_type"]?.GetValue<string>() == classType)
                return (obj, id);
        }
        return null;
    }

    private static string? LinkedNodeId(JsonObject node, string inputName) =>
        (node["inputs"]?[inputName] as JsonArray)?[0]?.GetValue<string>();

    private static void SetClipText(JsonObject graph, string nodeId, string text)
    {
        if (graph[nodeId] is not JsonObject node || node["inputs"] is not JsonObject)
            throw new InvalidOperationException($"ComfyUI workflow template references node {nodeId}, which doesn't exist.");
        node["inputs"]!["text"] = text;
    }
}
