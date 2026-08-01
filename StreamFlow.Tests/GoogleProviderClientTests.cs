using StreamFlow.App.Services.AI.Contracts;
using StreamFlow.App.Services.AI.Providers;

using Xunit;

namespace StreamFlow.Tests;

public class GoogleProviderClientTests
{
    [Fact]
    public void BuildGenerateContentRequest_UsesModelInUrlAndApiKeyAsQueryParam()
    {
        var req = GoogleProviderClient.BuildGenerateContentRequest("my-key", new TextGenerationRequest("gemini-1.5-pro", "Hello"));

        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-pro:generateContent?key=my-key",
            req.RequestUri!.ToString());
    }

    [Fact]
    public void ParseGenerateContentResponse_ExtractsFirstTextPart()
    {
        const string json = """{"candidates":[{"content":{"parts":[{"text":"Hello there"}]}}]}""";
        var result = GoogleProviderClient.ParseGenerateContentResponse(json);

        Assert.True(result.Success);
        Assert.Equal("Hello there", result.Text);
    }

    [Fact]
    public void ParseGenerateContentResponse_SurfacesErrorMessage()
    {
        const string json = """{"error":{"code":400,"message":"API key not valid"}}""";
        var result = GoogleProviderClient.ParseGenerateContentResponse(json);

        Assert.False(result.Success);
        Assert.Equal("API key not valid", result.ErrorMessage);
    }

    [Fact]
    public void ParseModelsResponse_StripsModelsPrefix()
    {
        const string json = """{"models":[{"name":"models/gemini-1.5-pro"},{"name":"models/gemini-1.5-flash"}]}""";
        var result = GoogleProviderClient.ParseModelsResponse(json);

        Assert.True(result.Success);
        Assert.Equal(["gemini-1.5-pro", "gemini-1.5-flash"], result.AvailableModels);
    }

    [Fact]
    public void ParseImageResponse_DecodesInlineBase64Data()
    {
        var payload = Convert.ToBase64String("fake-image-bytes"u8.ToArray());
        var json = "{\"candidates\":[{\"content\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"image/png\",\"data\":\"" + payload + "\"}}]}}]}";
        var result = GoogleProviderClient.ParseImageResponse(json);

        Assert.True(result.Success);
        Assert.Equal("fake-image-bytes"u8.ToArray(), result.Images[0]);
    }
}
