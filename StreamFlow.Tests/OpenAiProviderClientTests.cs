using StreamFlow.App.Services.AI.Contracts;
using StreamFlow.App.Services.AI.Providers;

using Xunit;

namespace StreamFlow.Tests;

public class OpenAiProviderClientTests
{
    [Fact]
    public void BuildChatRequest_SetsBearerAuthAndEndpoint()
    {
        var req = OpenAiProviderClient.BuildChatRequest("sk-test", new TextGenerationRequest("gpt-4o", "Hello"));

        Assert.Equal("https://api.openai.com/v1/chat/completions", req.RequestUri!.ToString());
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Equal("sk-test", req.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task BuildChatRequest_IncludesSystemPromptWhenGiven()
    {
        var req = OpenAiProviderClient.BuildChatRequest("sk-test", new TextGenerationRequest("gpt-4o", "Hello", SystemPrompt: "Be terse."));
        var body = await req.Content!.ReadAsStringAsync();

        Assert.Contains("\"role\":\"system\"", body);
        Assert.Contains("Be terse.", body);
    }

    [Fact]
    public void ParseChatResponse_ExtractsCompletionText()
    {
        const string json = """{"choices":[{"message":{"role":"assistant","content":"Hi there!"}}]}""";
        var result = OpenAiProviderClient.ParseChatResponse(json);

        Assert.True(result.Success);
        Assert.Equal("Hi there!", result.Text);
    }

    [Fact]
    public void ParseChatResponse_SurfacesApiErrorMessage()
    {
        const string json = """{"error":{"message":"Invalid API key","type":"invalid_request_error"}}""";
        var result = OpenAiProviderClient.ParseChatResponse(json);

        Assert.False(result.Success);
        Assert.Equal("Invalid API key", result.ErrorMessage);
    }

    [Fact]
    public void ParseModelsResponse_ExtractsSortedModelIds()
    {
        const string json = """{"data":[{"id":"gpt-4o"},{"id":"dall-e-3"}]}""";
        var result = OpenAiProviderClient.ParseModelsResponse(json);

        Assert.True(result.Success);
        Assert.Equal(["dall-e-3", "gpt-4o"], result.AvailableModels);
    }

    [Fact]
    public void ParseImageResponse_DecodesBase64Images()
    {
        var payload = Convert.ToBase64String("fake-png-bytes"u8.ToArray());
        var json = $$"""{"data":[{"b64_json":"{{payload}}"}]}""";
        var result = OpenAiProviderClient.ParseImageResponse(json);

        Assert.True(result.Success);
        Assert.Single(result.Images);
        Assert.Equal("fake-png-bytes"u8.ToArray(), result.Images[0]);
    }

    [Fact]
    public void ParseImageResponse_FailsCleanly_WhenOnlyUrlIsReturned()
    {
        const string json = """{"data":[{"url":"https://example.com/image.png"}]}""";
        var result = OpenAiProviderClient.ParseImageResponse(json);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }
}
