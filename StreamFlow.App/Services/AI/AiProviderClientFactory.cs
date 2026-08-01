using System.IO;

using StreamFlow.App.Services.AI.Contracts;
using StreamFlow.App.Services.AI.Providers;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.Core.Data.Ai;

namespace StreamFlow.App.Services.AI;

/// <summary>Turns an AiProviderProfile + its resolved API key into the right client adapter(s).
/// Returns null for a modality/provider combination that isn't supported (e.g. an image client
/// for Anthropic) rather than throwing — callers should already be filtering by
/// AiProviderProfile.SupportsText/SupportsImage before ever reaching here, so a null here means a
/// caller skipped that check.</summary>
public static class AiProviderClientFactory
{
    public static ITextGenerationClient? CreateTextClient(AiProviderProfile profile, string? apiKey) =>
        profile.Kind switch
        {
            AiProviderKind.OpenAI => new OpenAiProviderClient(apiKey ?? ""),
            AiProviderKind.Anthropic => new AnthropicProviderClient(apiKey ?? ""),
            AiProviderKind.Google => new GoogleProviderClient(apiKey ?? ""),
            AiProviderKind.Ollama => new OllamaProviderClient(profile.BaseUrl, NullIfEmpty(apiKey)),
            AiProviderKind.LmStudio => new LmStudioProviderClient(profile.BaseUrl, NullIfEmpty(apiKey)),
            _ => null,
        };

    public static IImageGenerationClient? CreateImageClient(AiProviderProfile profile, string? apiKey) =>
        profile.Kind switch
        {
            AiProviderKind.OpenAI => new OpenAiProviderClient(apiKey ?? ""),
            AiProviderKind.Google => new GoogleProviderClient(apiKey ?? ""),
            AiProviderKind.Automatic1111 => new Automatic1111ProviderClient(profile.BaseUrl),
            AiProviderKind.ComfyUi => new ComfyUiProviderClient(profile.BaseUrl,
                loadWorkflowJson: BuildWorkflowLoader(profile.ComfyUiWorkflowTemplatePath)),
            _ => null,
        };

    private static Func<string>? BuildWorkflowLoader(string? customPath) =>
        string.IsNullOrEmpty(customPath) ? null : () => File.ReadAllText(customPath);

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
