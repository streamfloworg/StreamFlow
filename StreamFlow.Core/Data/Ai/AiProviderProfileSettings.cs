namespace StreamFlow.Core.Data.Ai;

/// <summary>Persisted (non-secret) shape of one configured AI provider — mirrors
/// StreamingProfileSettings' multi-instance named-profile pattern. The API key/credential lives
/// separately in the DPAPI-encrypted ai_provider_keys.dat (see AppModel.LoadAiProviderKeys),
/// keyed by this record's Id — never in this settings JSON.</summary>
public sealed class AiProviderProfileSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Provider";
    public AiProviderKind Kind { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>Local providers only (Ollama/LM Studio/Automatic1111/ComfyUI) — the user's
    /// already-running server. Null for cloud providers, which use a fixed endpoint.</summary>
    public string? BaseUrl { get; set; }

    public string? DefaultModelText { get; set; }
    public string? DefaultModelImage { get; set; }

    /// <summary>ComfyUI only — path to a custom workflow JSON. Null uses the bundled default
    /// txt2img template (see ComfyUiWorkflowTemplate).</summary>
    public string? ComfyUiWorkflowTemplatePath { get; set; }
}
