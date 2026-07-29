
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using StreamFlow.Core.Data;

using StreamFlow.App.Views.Windows;

namespace StreamFlow.App.Services;
/// <summary>
/// Managed host of the application.
/// </summary>
public class ApplicationHostService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    private Window? _hostWindow;

    public ApplicationHostService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Triggered when the application host is ready to start the service.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the start process has been aborted.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await HandleActivationAsync();
    }

    /// <summary>
    /// Triggered when the application host is performing a graceful shutdown.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the shutdown process should no longer be graceful.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Creates main window during activation.
    /// </summary>
    private async Task HandleActivationAsync()
    {
        if (!System.Windows.Application.Current.Windows.OfType<MainWindow>().Any())
        {
            var pluginManager = _serviceProvider.GetService<StreamFlow.App.Services.Overlays.Plugins.PluginManagerService>();
            pluginManager?.LoadPlugins();

            _hostWindow = (
                _serviceProvider.GetService(typeof(Window)) as Window
            )!;
            _hostWindow.Show();
        }

        await Task.CompletedTask;
    }
}
