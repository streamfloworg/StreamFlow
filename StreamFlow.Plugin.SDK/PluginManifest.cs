using System.Text.Json.Serialization;

namespace StreamFlow.Plugin.SDK;

public sealed class PluginManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("entryDll")]
    public string EntryDll { get; set; } = string.Empty;

    [JsonPropertyName("minAppVersion")]
    public string MinAppVersion { get; set; } = "1.0.0";

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = string.Empty;
}
