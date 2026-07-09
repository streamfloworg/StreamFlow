using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Threading;

using AdonisUI.Controls;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using StreamFlow.App.Services;
using StreamFlow.App.Services.Core;
using StreamFlow.App.ViewModels;
using StreamFlow.App.ViewModels.Pages;
using StreamFlow.App.ViewModels.Windows;
using StreamFlow.App.Views.Pages;
using StreamFlow.App.Views.Windows;
using StreamFlow.Core.Data;
using StreamFlow.Core.Persistence;

namespace StreamFlow.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : IDisposable
{

    private SingleInstanceManager? _singleInstanceManager;
    private string[]? _startupArgs;

    private static readonly IHost _host = Host
        .CreateDefaultBuilder()
        .ConfigureAppConfiguration(c => {
            c.SetBasePath(AppContext.BaseDirectory);
            c.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            // Secrets (OAuth client secrets, etc.) live here instead — this repo is public,
            // and this file is gitignored, unlike appsettings.json.
            c.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
        })
        .ConfigureServices((context, services) =>
        {
            services.AddHostedService<ApplicationHostService>();
            services.AddSingleton<CoreBridgeService>();
            services.AddHostedService(p => p.GetRequiredService<CoreBridgeService>());

            services.AddSingleton<AppModel>();

            // Main window and view model
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<Window, MainWindow>();


            services.AddSingleton<IPersistenceDataManager, PersistenceJsonDataManager>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<TwitchAuthService>();
            services.AddSingleton<YouTubeAuthService>();
            services.AddSingleton<TwitchChatService>();
            services.AddSingleton<YouTubeChatService>();
            services.AddSingleton<GoLiveSettingsService>();
            services.AddSingleton<SceneSetService>();
            services.AddSingleton<GpuEncoderDetectionService>();
            services.AddSingleton<UpdateService>();


            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<SettingsPage>();
#if DEBUG
            services.AddSingleton<DebugViewModel>();
#endif

            services.AddSingleton<SceneEditorViewModel>();

            services.AddSingleton<GoLiveViewModel>();
            services.AddSingleton<GoLiveView>();

            services.AddSingleton<AudioViewModel>();
            services.AddSingleton<AudioView>();

            services.AddSingleton<ScenesViewModel>();
            services.AddSingleton<ScenesView>();

            services.AddSingleton<ComposeViewModel>();
            services.AddSingleton<ComposeView>();

            // URI Protocol Handler
            services.AddSingleton<ProtocolHandlerService>(sp =>
            {
                var audioViewModel = sp.GetRequiredService<AudioViewModel>();
                var logger = sp.GetService<ILogger<ProtocolHandlerService>>();
                return new ProtocolHandlerService(audioViewModel, logger);
            });
        }).Build();

    /// <summary>
    /// Gets services.
    /// </summary>
    public static IServiceProvider Services => _host.Services;

    /// <summary>
    /// Occurs when the application is loading.
    /// </summary>
    private async void OnStartup(object sender, StartupEventArgs e)
    {
        // Must be the very first thing that runs, before anything else (single-instance check,
        // protocol registration, etc.) — Velopack relaunches the freshly installed/updated exe
        // with hidden --veloapp-* flags during install/update/uninstall, and this call detects
        // those, runs the matching hook (e.g. Start Menu shortcut creation), and exits
        // immediately. On a normal launch (no such flag), it's a no-op and returns right away.
        // Only meaningful for the unpackaged/Velopack distribution — a no-op under MSIX, which
        // manages install/shortcuts/updates itself.
        Velopack.VelopackApp.Build().Run();

        Helpers.NotificationHelper.RegisterAumidAndCreateShortcut("com.nomadamo.streamflow", "StreamFlow");

        // Initialize single instance manager
        _singleInstanceManager = new SingleInstanceManager();

        if (!_singleInstanceManager.IsFirstInstance)
        {
            // This is a second instance - send args to first instance and exit
            if (e.Args.Length > 0)
            {
                await SingleInstanceManager.SendToFirstInstanceAsync(e.Args);
            }
            Shutdown();
            return;
        }

        // This is the first instance - register protocol if needed
        if (!ProtocolRegistration.IsProtocolRegistered())
        {
            var registered = ProtocolRegistration.RegisterProtocol();
            if (registered)
            {
                Debug.WriteLine("streamflow:// protocol registered successfully");
            }
            else
            {
                Debug.WriteLine("Failed to register streamflow:// protocol");
            }
        }

        // Subscribe to arguments from other instances
        _singleInstanceManager.ArgumentsReceived += OnArgumentsReceived;

        // Store startup args for processing after host is ready
        _startupArgs = e.Args;

        //Listen to notification activation
        await _host.StartAsync();

        // Process startup args after host is started
        if (_startupArgs != null && _startupArgs.Length > 0)
        {
            await ProcessCommandLineArgsAsync(_startupArgs);
            _startupArgs = null;
        }
    }

    /// <summary>
    /// Occurs when the application is closing.
    /// </summary>
    private void OnExit(object sender, ExitEventArgs e)
    {
        _singleInstanceManager?.Dispose();

        // Must block here rather than `await`: WPF does not wait for async void Exit handlers
        // to complete, so an awaited StopAsync() could get torn down mid-flight before
        // CoreBridgeService.StopAsync ever kills streamflow-core.exe — orphaning it, which then
        // holds a file lock that makes the next build's Copy of streamflow-core.exe fail.
        // Run via Task.Run rather than blocking in place: StopAsync's continuations don't all
        // use ConfigureAwait(false), and GetAwaiter().GetResult() directly on the UI thread
        // deadlocks when a continuation tries to resume on that same (blocked) dispatcher.
        //
        // Bounded (not GetAwaiter().GetResult(), which blocks forever): every await inside
        // StopAsync is itself now bounded too, but this is the last line of defense against a
        // genuinely stuck child process turning "the window closed" into "the process never
        // actually exits" — the exact symptom that motivated this: no window/taskbar icon, but
        // the process (and an orphaned streamflow-core.exe) still resident in Task Manager.
        try
        {
            var stopped = Task.Run(() => _host.StopAsync()).Wait(TimeSpan.FromSeconds(10));
            if (stopped)
            {
                _host.Dispose();
            }
            else
            {
                Debug.WriteLine("Host shutdown timed out — forcing process exit without a clean Dispose.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error during host shutdown: {ex.Message}");
        }

        // Guarantees the process actually terminates regardless of what happened above.
        Environment.Exit(0);
    }

    /// <summary>
    /// Occurs when an exception is thrown by an application but not handled.
    /// </summary>
    public void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (Current.MainWindow is MainWindow mw)
        {
            if (Current.MainWindow is not null && !mw.IsVisible)
            {
                mw.StartExceptions.Add(e);
            }
            else
            {
                mw.OnDispatcherUnhandledException(sender, e);
            }
        }
    }

    public static bool IsMultiThreaded { get; }

    public static TEnum GetEnum<TEnum>(string text) where TEnum : struct
    {
        if (!typeof(TEnum).GetTypeInfo().IsEnum)
        {
            throw new InvalidOperationException("Generic parameter 'TEnum' must be an enum.");
        }
        return Enum.Parse<TEnum>(text);
    }

    public static Process BrowseWeb(string path)
    {
        try
        {
            return Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.ToString());
            return null;
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Command-line argument processing is now handled in the async OnStartup method above
    }

    /// <summary>
    /// Handles arguments received from another instance
    /// </summary>
    private async void OnArgumentsReceived(object? sender, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        // Parse arguments (separated by newlines)
        var args = message.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

        // Bring window to front
        if (Current.MainWindow is MainWindow mw)
        {
            mw.Dispatcher.Invoke(() =>
            {
                if (mw.WindowState == System.Windows.WindowState.Minimized)
                    mw.WindowState = System.Windows.WindowState.Normal;
                mw.Activate();
                mw.Focus();
            });
        }

        // Process the arguments
        await ProcessCommandLineArgsAsync(args);
    }

    /// <summary>
    /// Processes command-line arguments for file paths and streamflow:// URIs
    /// </summary>
    private async System.Threading.Tasks.Task ProcessCommandLineArgsAsync(string[] args)
    {
        if (args.Length == 0)
            return;

        var protocolHandler = Services.GetService<ProtocolHandlerService>();

        foreach (var arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg))
                continue;

            // Check if it's a streamflow:// URI
            if (arg.StartsWith("streamflow://", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("streamflow:", StringComparison.OrdinalIgnoreCase))
            {
                if (protocolHandler != null)
                {
                    await protocolHandler.HandleUriAsync(arg);
                }
                else
                {
                    Debug.WriteLine("ProtocolHandlerService not available");
                }
            }
            // Check if it's a file path
            else if (File.Exists(arg))
            {
                if (Current.MainWindow is MainWindow)
                {
                    Views.Windows.MainWindow.ShowNotification("Opening file", $"Opened file: {arg}", InfoBarSeverity.Informational);
                }
            }
        }
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        Debug.WriteLine("Process: " + Environment.ProcessPath);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    internal static partial class AppIdHelper
    {
        [LibraryImport("shell32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int SetCurrentProcessExplicitAppUserModelID(string appID);

        public static void EnsureAppUserModelId()
        {
            // Choose a unique app id, e.g. "com.yourcompany.streamflow"
            _ = SetCurrentProcessExplicitAppUserModelID("com.nomadamo.streamflow");
        }
    }
}
