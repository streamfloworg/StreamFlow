namespace StreamFlow.Core.Data.Ai;

/// <summary>Top-level AI provider configuration — a sibling of GoLiveSettings on PersistentData,
/// not nested inside it, since this is about AI provider connectivity rather than scenes/
/// streaming. See AppModel.LoadAiProviderKeys/SaveAiProviderKeys for where the actual API keys
/// live (never in this object).</summary>
public sealed class AiSettings
{
    public List<AiProviderProfileSettings> Providers { get; set; } = [];
    public string? DefaultTextProviderId { get; set; }
    public string? DefaultImageProviderId { get; set; }
}
