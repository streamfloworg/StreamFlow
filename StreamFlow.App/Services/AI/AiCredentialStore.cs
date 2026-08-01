using System.IO;
using System.Security.Cryptography;
using System.Text;

using Newtonsoft.Json;

using StreamFlow.Core.Data;

namespace StreamFlow.App.Services.AI;

/// <summary>Stores/retrieves AI provider API keys, DPAPI-encrypted (DataProtectionScope.CurrentUser)
/// the same way AppModel's stream_keys.dat does, keyed by AiProviderProfile.Id. Adds the
/// per-profile add/remove semantics a "Connect"/"Disconnect" UI needs — a Disconnect must only
/// drop that one profile's entry, not the whole file.
///
/// Production code (the default, no-arg constructor) routes through
/// AppModel.LoadAiProviderKeys/SaveAiProviderKeys — the one place the real ai_provider_keys.dat
/// path/DPAPI logic lives, mirroring stream_keys.dat. The explicit-path constructor overload
/// exists solely so tests can round-trip against a temp file instead of the real user profile's
/// DPAPI store; it duplicates the same small DPAPI idiom directly rather than making AppModel's
/// methods path-configurable, since nothing else needs that.</summary>
public sealed class AiCredentialStore
{
    private readonly string? _testFilePath;

    /// <summary>Not resolved until actually needed (see LoadAll/SaveAll) — AppModel.Instance
    /// touches WPF's ObservableCollection/Dispatcher machinery on first access, which only exists
    /// once a real Application/UI thread is running. Resolving it eagerly in this constructor
    /// would make this class (and anything that DI-resolves it, including a plain
    /// ServiceCollection unit test with no Dispatcher) blow up outside that context.</summary>
    public AiCredentialStore()
    {
    }

    /// <summary>Test-only: round-trips DPAPI-encrypted keys against <paramref name="filePath"/>
    /// instead of the real ai_provider_keys.dat.</summary>
    public AiCredentialStore(string filePath)
    {
        _testFilePath = filePath;
    }

    public Dictionary<string, string> LoadAll() =>
        _testFilePath is not null ? LoadFrom(_testFilePath) : AppModel.Instance.LoadAiProviderKeys();

    private void SaveAll(Dictionary<string, string> keys)
    {
        if (_testFilePath is not null) SaveTo(_testFilePath, keys);
        else AppModel.Instance.SaveAiProviderKeys(keys);
    }

    public string? GetApiKey(string profileId) =>
        LoadAll().TryGetValue(profileId, out var key) ? key : null;

    public void SetApiKey(string profileId, string apiKey)
    {
        var keys = LoadAll();
        keys[profileId] = apiKey;
        SaveAll(keys);
    }

    public void RemoveApiKey(string profileId)
    {
        var keys = LoadAll();
        if (keys.Remove(profileId))
            SaveAll(keys);
    }

    private static Dictionary<string, string> LoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(Encoding.UTF8.GetString(bytes)) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void SaveTo(string path, Dictionary<string, string> keys)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(keys));
        File.WriteAllBytes(path, ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
    }
}
