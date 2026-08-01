using StreamFlow.App.Services.AI.Contracts;
using StreamFlow.App.Services.AI.Providers;

using Xunit;

namespace StreamFlow.Tests;

public class AnthropicProviderClientTests
{
    [Fact]
    public void BuildMessagesRequest_SetsApiKeyAndVersionHeaders()
    {
        var req = AnthropicProviderClient.BuildMessagesRequest("sk-ant-test", new TextGenerationRequest("claude-3-5-sonnet", "Hello"));

        Assert.Equal("https://api.anthropic.com/v1/messages", req.RequestUri!.ToString());
        Assert.Equal("sk-ant-test", req.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", req.Headers.GetValues("anthropic-version").Single());
    }

    [Fact]
    public async Task BuildMessagesRequest_DefaultsMaxTokens_WhenNotSpecified()
    {
        var req = AnthropicProviderClient.BuildMessagesRequest("sk-ant-test", new TextGenerationRequest("claude-3-5-sonnet", "Hello"));
        var body = await req.Content!.ReadAsStringAsync();

        Assert.Contains("\"max_tokens\":1024", body);
    }

    [Fact]
    public void ParseMessagesResponse_ConcatenatesTextBlocks()
    {
        const string json = """{"content":[{"type":"text","text":"Hello"},{"type":"text","text":" world"}]}""";
        var result = AnthropicProviderClient.ParseMessagesResponse(json);

        Assert.True(result.Success);
        Assert.Equal("Hello world", result.Text);
    }

    [Fact]
    public void ParseMessagesResponse_SurfacesErrorMessage()
    {
        const string json = """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""";
        var result = AnthropicProviderClient.ParseMessagesResponse(json);

        Assert.False(result.Success);
        Assert.Equal("invalid x-api-key", result.ErrorMessage);
    }

    [Fact]
    public void ParseModelsResponse_ExtractsModelIds()
    {
        const string json = """{"data":[{"id":"claude-3-5-sonnet-20241022"},{"id":"claude-3-opus-20240229"}]}""";
        var result = AnthropicProviderClient.ParseModelsResponse(json);

        Assert.True(result.Success);
        Assert.Equal(2, result.AvailableModels.Count);
    }
}
