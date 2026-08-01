using StreamFlow.App.Services.AI;

using Xunit;

namespace StreamFlow.Tests;

public class AiCredentialStoreTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"ai_provider_keys_test_{Guid.NewGuid():N}.dat");

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    [Fact]
    public void SetApiKey_ThenGetApiKey_RoundTrips()
    {
        var store = new AiCredentialStore(_tempFile);
        store.SetApiKey("profile-1", "sk-abc123");

        Assert.Equal("sk-abc123", store.GetApiKey("profile-1"));
    }

    [Fact]
    public void GetApiKey_ReturnsNull_ForUnknownProfile()
    {
        var store = new AiCredentialStore(_tempFile);
        Assert.Null(store.GetApiKey("does-not-exist"));
    }

    [Fact]
    public void RemoveApiKey_OnlyRemovesThatProfile_OthersSurvive()
    {
        var store = new AiCredentialStore(_tempFile);
        store.SetApiKey("profile-1", "key-1");
        store.SetApiKey("profile-2", "key-2");

        store.RemoveApiKey("profile-1");

        Assert.Null(store.GetApiKey("profile-1"));
        Assert.Equal("key-2", store.GetApiKey("profile-2"));
    }

    [Fact]
    public void FileOnDisk_IsNotPlaintext()
    {
        var store = new AiCredentialStore(_tempFile);
        store.SetApiKey("profile-1", "sk-super-secret-value");

        var rawBytes = File.ReadAllBytes(_tempFile);
        var rawText = System.Text.Encoding.UTF8.GetString(rawBytes);
        Assert.DoesNotContain("sk-super-secret-value", rawText);
    }

    [Fact]
    public void NewStore_WithNoExistingFile_LoadsEmpty()
    {
        var store = new AiCredentialStore(_tempFile);
        Assert.Empty(store.LoadAll());
    }
}
