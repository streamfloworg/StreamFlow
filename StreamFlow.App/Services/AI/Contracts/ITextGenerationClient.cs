using StreamFlow.Core.Data.Ai;

namespace StreamFlow.App.Services.AI.Contracts;

public interface ITextGenerationClient
{
    AiProviderKind Kind { get; }

    Task<AiConnectionTestResult> TestConnectionAsync(CancellationToken ct = default);

    Task<TextGenerationResult> GenerateTextAsync(TextGenerationRequest request, CancellationToken ct = default);
}
