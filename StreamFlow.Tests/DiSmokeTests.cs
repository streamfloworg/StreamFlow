using Microsoft.Extensions.DependencyInjection;
using StreamFlow.App.Services;

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
}
