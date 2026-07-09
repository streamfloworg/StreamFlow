using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace StreamFlow.App.Services;

/// <summary>
/// Manages single instance behavior for StreamFlow application
/// </summary>
/// <remarks>
/// Ensures only one instance of StreamFlow runs at a time. When a second instance is launched,
/// it sends its command-line arguments to the first instance via named pipe and exits.
/// </remarks>
public class SingleInstanceManager : IDisposable
{
    private const string PipeName = "StreamFlow_SingleInstance_Pipe";
    private const string MutexName = "StreamFlow_SingleInstance_Mutex";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private bool _isFirstInstance;

    public event EventHandler<string>? ArgumentsReceived;

    public bool IsFirstInstance => _isFirstInstance;

    public SingleInstanceManager()
    {
        _cancellationTokenSource = new CancellationTokenSource();

        // Try to create or open a named mutex
        _mutex = new Mutex(true, MutexName, out _isFirstInstance);

        if (_isFirstInstance)
        {
            // This is the first instance - start listening for other instances
            // The pipe server is created fresh for each connection in the listener
            Task.Run(() => ListenForConnectionsAsync(_cancellationTokenSource.Token));
        }
    }

    /// <summary>
    /// Sends arguments to the first instance
    /// </summary>
    /// <param name="args">Command-line arguments to send</param>
    /// <returns>True if successfully sent, false otherwise</returns>
    public static async Task<bool> SendToFirstInstanceAsync(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);

            // Try to connect with timeout (5 seconds = 5000 milliseconds)
            await client.ConnectAsync(5000);

            if (!client.IsConnected)
            {
                return false;
            }

            // Send arguments as a single line (joined by newlines)
            var message = string.Join(Environment.NewLine, args);
            var messageBytes = Encoding.UTF8.GetBytes(message);

            await client.WriteAsync(messageBytes, 0, messageBytes.Length);
            await client.FlushAsync();

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error sending to first instance: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Listens for connections from other instances
    /// </summary>
    private async Task ListenForConnectionsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? currentPipeServer = null;

            try
            {
                // Create a new pipe server for each connection
                currentPipeServer = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1, // Max 1 connection at a time
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous);

                System.Diagnostics.Debug.WriteLine("Named pipe server waiting for connection...");

                // Wait for a connection
                await currentPipeServer.WaitForConnectionAsync(cancellationToken);

                System.Diagnostics.Debug.WriteLine("Named pipe server received connection");

                if (currentPipeServer.IsConnected)
                {
                    // Read the message
                    using var reader = new StreamReader(currentPipeServer, Encoding.UTF8, leaveOpen: true);
                    var message = await reader.ReadToEndAsync(cancellationToken);

                    System.Diagnostics.Debug.WriteLine($"Named pipe received message: {message}");

                    // Notify listeners on the UI thread
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        ArgumentsReceived?.Invoke(this, message);
                    });

                    // Disconnect this instance
                    currentPipeServer.Disconnect();
                }

                // Dispose the current pipe server before creating a new one
                currentPipeServer.Dispose();
                currentPipeServer = null;
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
                System.Diagnostics.Debug.WriteLine("Named pipe server cancelled");
                break;
            }
            catch (ObjectDisposedException)
            {
                // Pipe was disposed - exit gracefully
                System.Diagnostics.Debug.WriteLine("Named pipe server disposed");
                break;
            }
            catch (IOException ex) when (ex.Message.Contains("pipe", StringComparison.OrdinalIgnoreCase))
            {
                // Pipe-related IO errors during shutdown
                System.Diagnostics.Debug.WriteLine($"Pipe IO error (likely during shutdown): {ex.Message}");
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in pipe server: {ex.Message}");

                // Clean up the current pipe if there was an error
                currentPipeServer?.Dispose();
                currentPipeServer = null;

                // Wait a bit before trying again (only if not shutting down)
                if (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(1000, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }
        }

        System.Diagnostics.Debug.WriteLine("Named pipe server listener exited gracefully");
    }

    public void Dispose()
    {
        try
        {
            // Cancel the background task first
            _cancellationTokenSource?.Cancel();

            // Give the listener a moment to exit gracefully
            Task.Delay(100).Wait();

            // Release and dispose mutex
            if (_isFirstInstance)
            {
                _mutex?.ReleaseMutex();
            }
            _mutex?.Dispose();

            // Dispose cancellation token source
            _cancellationTokenSource?.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error during SingleInstanceManager disposal: {ex.Message}");
        }
    }
}
