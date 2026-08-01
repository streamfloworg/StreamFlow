using Microsoft.Extensions.DependencyInjection;
using StreamFlow.App.Services;
using StreamFlow.App.Services.AI;

namespace StreamFlow.App.Tests;

public class DiSmokeTests
{
    [Fact]
    public void ServiceProvider_Resolves_UpdateService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<UpdateService>();

        var provider = services.BuildServiceProvider();

        var service = provider.GetRequiredService<UpdateService>();
        Assert.NotNull(service);
    }

    [Fact]
    public void ServiceProvider_Resolves_AiCredentialStoreAndRegistry()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<AiCredentialStore>();
        services.AddSingleton<AiProviderRegistryService>();

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<AiCredentialStore>());
        Assert.NotNull(provider.GetRequiredService<AiProviderRegistryService>());
    }
}
