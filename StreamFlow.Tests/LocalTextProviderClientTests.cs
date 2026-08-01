using StreamFlow.App.Services.AI.Contracts;
using StreamFlow.App.Services.AI.Providers;

using Xunit;

namespace StreamFlow.Tests;

/// <summary>Covers Ollama and LM Studio together with the shared OpenAI-compatible chat request/
/// response logic they both inherit from OpenAiCompatibleLocalClientBase, plus each one's own
/// model-listing endpoint/parsing (the one thing that actually differs between them).</summary>
public class LocalTextProviderClientTests
{
    [Fact]
    public void Ollama_BuildChatRequest_HitsLocalV1ChatCompletions()
    {
        var client = new OllamaProviderClient("http://localhost:11434");
        var req = client.BuildChatRequest(new TextGenerationRequest("llama3", "Hello"));

        Assert.Equal("http://localhost:11434/v1/chat/completions", req.RequestUri!.ToString());
        Assert.Null(req.Headers.Authorization);
    }

    [Fact]
    public void Ollama_BuildChatRequest_SendsBearerToken_WhenApiKeyProvided()
    {
        var client = new OllamaProviderClient("http://localhost:11434", apiKey: "tunnel-key");
        var req = client.BuildChatRequest(new TextGenerationRequest("llama3", "Hello"));

        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Equal("tunnel-key", req.Headers.Authorization.Parameter);
    }

    [Fact]
    public void Ollama_BaseUrl_TrailingSlashIsTrimmed()
    {
        var client = new OllamaProviderClient("http://localhost:11434/");
        var req = client.BuildTagsRequest();

        Assert.Equal("http://localhost:11434/api/tags", req.RequestUri!.ToString());
    }

    [Fact]
    public void Ollama_ParseTagsResponse_ExtractsModelNames()
    {
        const string json = """{"models":[{"name":"llama3:latest"},{"name":"mistral:latest"}]}""";
        var result = OllamaProviderClient.ParseTagsResponse(json);

        Assert.True(result.Success);
        Assert.Equal(["llama3:latest", "mistral:latest"], result.AvailableModels);
    }

    [Fact]
    public void LmStudio_BuildModelsRequest_HitsV1Models()
    {
        var client = new LmStudioProviderClient("http://localhost:1234");
        var req = client.BuildModelsRequest();

        Assert.Equal("http://localhost:1234/v1/models", req.RequestUri!.ToString());
    }

    [Fact]
    public void LmStudio_ParseModelsResponse_ExtractsIds()
    {
        const string json = """{"data":[{"id":"local-model-a"},{"id":"local-model-b"}]}""";
        var result = LmStudioProviderClient.ParseModelsResponse(json);

        Assert.True(result.Success);
        Assert.Equal(["local-model-a", "local-model-b"], result.AvailableModels);
    }

    [Fact]
    public void ParseChatResponse_SharedAcrossOllamaAndLmStudio_ExtractsCompletionText()
    {
        const string json = """{"choices":[{"message":{"content":"Hi from a local model"}}]}""";
        var result = OpenAiCompatibleLocalClientBase.ParseChatResponse(json);

        Assert.True(result.Success);
        Assert.Equal("Hi from a local model", result.Text);
    }
}
