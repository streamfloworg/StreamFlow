using StreamFlow.Core.Data.Ai;

namespace StreamFlow.App.Services.AI.Contracts;

public interface IImageGenerationClient
{
    AiProviderKind Kind { get; }

    Task<AiConnectionTestResult> TestConnectionAsync(CancellationToken ct = default);

    Task<ImageGenerationResult> GenerateImageAsync(ImageGenerationRequest request, CancellationToken ct = default);
}
