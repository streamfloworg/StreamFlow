using StreamFlow.App.Services.AI.Contracts;
using StreamFlow.App.Services.AI.Providers;

using Xunit;

namespace StreamFlow.Tests;

public class Automatic1111ProviderClientTests
{
    [Fact]
    public void BuildTxt2ImgRequest_HitsSdApiEndpoint_WithTrimmedBaseUrl()
    {
        var client = new Automatic1111ProviderClient("http://localhost:7860/");
        var req = client.BuildTxt2ImgRequest(new ImageGenerationRequest(null, "a cat"));

        Assert.Equal("http://localhost:7860/sdapi/v1/txt2img", req.RequestUri!.ToString());
    }

    [Fact]
    public async Task BuildTxt2ImgRequest_IncludesPromptAndSize()
    {
        var client = new Automatic1111ProviderClient("http://localhost:7860");
        var req = client.BuildTxt2ImgRequest(new ImageGenerationRequest(null, "a cat", Width: 768, Height: 512));
        var body = await req.Content!.ReadAsStringAsync();

        Assert.Contains("\"prompt\":\"a cat\"", body);
        Assert.Contains("\"width\":768", body);
        Assert.Contains("\"height\":512", body);
    }

    [Fact]
    public void ParseTxt2ImgResponse_DecodesBase64Images()
    {
        var payload = Convert.ToBase64String("fake-png"u8.ToArray());
        var json = $$"""{"images":["{{payload}}"],"parameters":{},"info":"{}"}""";
        var result = Automatic1111ProviderClient.ParseTxt2ImgResponse(json);

        Assert.True(result.Success);
        Assert.Equal("fake-png"u8.ToArray(), result.Images[0]);
    }

    [Fact]
    public void ParseModelsResponse_ExtractsModelNames()
    {
        const string json = """[{"title":"model_a.safetensors [abc123]","model_name":"model_a"},{"title":"model_b.safetensors [def456]","model_name":"model_b"}]""";
        var result = Automatic1111ProviderClient.ParseModelsResponse(json);

        Assert.True(result.Success);
        Assert.Equal(["model_a", "model_b"], result.AvailableModels);
    }
}
