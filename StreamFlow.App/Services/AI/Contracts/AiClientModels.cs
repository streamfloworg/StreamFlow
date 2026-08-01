namespace StreamFlow.App.Services.AI.Contracts;

public sealed record TextGenerationRequest(
    string Model,
    string Prompt,
    string? SystemPrompt = null,
    double? Temperature = null,
    int? MaxTokens = null);

public sealed record TextGenerationResult(bool Success, string? Text, string? ErrorMessage)
{
    public static TextGenerationResult Ok(string text) => new(true, text, null);
    public static TextGenerationResult Failed(string errorMessage) => new(false, null, errorMessage);
}

public sealed record ImageGenerationRequest(
    string? Model,
    string Prompt,
    string? NegativePrompt = null,
    int Width = 1024,
    int Height = 1024,
    int? Seed = null,
    int? Steps = null);

public sealed record ImageGenerationResult(bool Success, IReadOnlyList<byte[]> Images, string? ErrorMessage)
{
    public static ImageGenerationResult Ok(IReadOnlyList<byte[]> images) => new(true, images, null);
    public static ImageGenerationResult Failed(string errorMessage) => new(false, [], errorMessage);
}

/// <summary>Result of a "Connect"/"Test Connection" call — a cheap models-list (or, for ComfyUI,
/// a bare reachability) request, not an actual generation.</summary>
public sealed record AiConnectionTestResult(bool Success, string? ErrorMessage, IReadOnlyList<string> AvailableModels)
{
    public static AiConnectionTestResult Ok(IReadOnlyList<string> models) => new(true, null, models);
    public static AiConnectionTestResult Failed(string errorMessage) => new(false, errorMessage, []);
}
