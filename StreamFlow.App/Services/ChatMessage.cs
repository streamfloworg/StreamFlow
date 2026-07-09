namespace StreamFlow.App.Services;

/// <summary>One chat line from either TwitchChatService or YouTubeChatService, normalized to a
/// common shape so the overlay renderer doesn't need to care which platform it came from.</summary>
/// <param name="ColorHex">"#RRGGBB", or null if the platform/user didn't supply one (the
/// renderer picks a fallback in that case).</param>
public sealed record ChatMessage(string Username, string Text, string? ColorHex);
