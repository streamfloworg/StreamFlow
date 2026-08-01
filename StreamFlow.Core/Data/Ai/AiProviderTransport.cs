namespace StreamFlow.Core.Data.Ai;

/// <summary>How a provider is reached — determines which credential fields the Settings UI shows
/// (see AiProviderProfile.IsLocal): CloudApiKey providers need an API key and have a fixed
/// endpoint; LocalHttp providers need a base URL (the user's already-running local server) and an
/// API key only for tunneled/remote setups.</summary>
public enum AiProviderTransport
{
    CloudApiKey,
    LocalHttp,
}
