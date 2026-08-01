namespace StreamFlow.Core.Data.Ai;

/// <summary>Static facts about one AI provider kind — which modalities it actually supports, how
/// it's reached, and any quirks the client adapter needs (e.g. ComfyUI needs a workflow
/// template). This is a compile-time catalog specifically so "Anthropic has no image generation
/// API" is a structural fact any UI/registry code reads, not a scattered `if` check that's easy
/// to miss when a new provider or modality is added.</summary>
public sealed record AiProviderCapabilityInfo(
    AiProviderKind Kind,
    string DisplayName,
    AiProviderTransport Transport,
    IReadOnlyList<AiModality> SupportedModalities,
    string? DefaultBaseUrl,
    bool RequiresWorkflowTemplate);

public static class AiProviderCatalog
{
    public static readonly IReadOnlyList<AiProviderCapabilityInfo> All =
    [
        new(AiProviderKind.OpenAI, "OpenAI", AiProviderTransport.CloudApiKey,
            [AiModality.Text, AiModality.Image], null, false),
        new(AiProviderKind.Anthropic, "Anthropic", AiProviderTransport.CloudApiKey,
            [AiModality.Text], null, false),
        new(AiProviderKind.Google, "Google (Gemini)", AiProviderTransport.CloudApiKey,
            [AiModality.Text, AiModality.Image], null, false),
        new(AiProviderKind.Ollama, "Ollama", AiProviderTransport.LocalHttp,
            [AiModality.Text], "http://localhost:11434", false),
        new(AiProviderKind.LmStudio, "LM Studio", AiProviderTransport.LocalHttp,
            [AiModality.Text], "http://localhost:1234", false),
        new(AiProviderKind.Automatic1111, "Automatic1111", AiProviderTransport.LocalHttp,
            [AiModality.Image], "http://localhost:7860", false),
        new(AiProviderKind.ComfyUi, "ComfyUI", AiProviderTransport.LocalHttp,
            [AiModality.Image], "http://localhost:8188", true),
    ];

    public static AiProviderCapabilityInfo For(AiProviderKind kind) =>
        All.First(c => c.Kind == kind);
}
