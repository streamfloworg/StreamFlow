using Microsoft.Extensions.DependencyInjection;
using StreamFlow.App.ViewModels.Pages;

namespace StreamFlow.App.Tests;

public class DiSmokeTests
{
    [Fact]
    public void ServiceProvider_Resolves_AudioViewModel()
    {
        var services = new ServiceCollection();
        services.AddSingleton<AudioViewModel>();

        var provider = services.BuildServiceProvider();

        var vm = provider.GetRequiredService<AudioViewModel>();
        Assert.NotNull(vm);

        // Execute the generated command via reflection
        var cmd = vm.GetType().GetProperty("IncrementCounterCommand")!.GetValue(vm)!;
        cmd.GetType().GetMethod("Execute")!.Invoke(cmd, new object?[] { null });
        Assert.Equal(1, vm.Count);
    }
}
