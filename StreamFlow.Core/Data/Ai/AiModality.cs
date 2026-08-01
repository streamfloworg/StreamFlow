namespace StreamFlow.Core.Data.Ai;

/// <summary>Generation modality an AI provider can support. Video and audio are deliberately
/// excluded for now — video-generation API access is too immature/restricted across cloud
/// providers to build against yet, and audio generation hasn't been scoped. Adding either later
/// is additive (new enum member + AiProviderCatalog rows), not a breaking change.</summary>
public enum AiModality
{
    Text,
    Image,
}
