using StreamFlow.Core.Data.Ai;
using Xunit;

namespace StreamFlow.Tests;

public class AiProviderCatalogTests
{
    [Theory]
    [InlineData(AiProviderKind.OpenAI)]
    [InlineData(AiProviderKind.Anthropic)]
    [InlineData(AiProviderKind.Google)]
    [InlineData(AiProviderKind.Ollama)]
    [InlineData(AiProviderKind.LmStudio)]
    [InlineData(AiProviderKind.Automatic1111)]
    [InlineData(AiProviderKind.ComfyUi)]
    public void Every_AiProviderKind_HasExactlyOneCatalogEntry(AiProviderKind kind)
    {
        var matches = AiProviderCatalog.All.Count(c => c.Kind == kind);
        Assert.Equal(1, matches);
    }

    [Fact]
    public void Anthropic_DoesNotSupportImage()
    {
        var anthropic = AiProviderCatalog.For(AiProviderKind.Anthropic);
        Assert.DoesNotContain(AiModality.Image, anthropic.SupportedModalities);
    }

    [Fact]
    public void OnlyComfyUi_RequiresWorkflowTemplate()
    {
        var requiring = AiProviderCatalog.All.Where(c => c.RequiresWorkflowTemplate).Select(c => c.Kind).ToList();
        Assert.Equal([AiProviderKind.ComfyUi], requiring);
    }

    [Fact]
    public void CloudProviders_HaveNoDefaultBaseUrl_LocalProvidersDo()
    {
        foreach (var info in AiProviderCatalog.All)
        {
            if (info.Transport == AiProviderTransport.CloudApiKey)
                Assert.Null(info.DefaultBaseUrl);
            else
                Assert.False(string.IsNullOrEmpty(info.DefaultBaseUrl));
        }
    }

    [Fact]
    public void EveryProvider_SupportsAtLeastOneModality()
    {
        foreach (var info in AiProviderCatalog.All)
            Assert.NotEmpty(info.SupportedModalities);
    }
}
