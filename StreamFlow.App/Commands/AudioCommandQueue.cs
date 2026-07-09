using System.Collections.Concurrent;

namespace StreamFlow.App.Commands;

/// <summary>
/// A thread-safe queue system designed to serialize the execution of audio commands 
/// from multiple threads onto a dedicated processing thread, ensuring deterministic state changes.
/// </summary>
public class AudioCommandQueue : Queue<AudioCommandBase>, IDisposable
{
    // The underlying queue, inherently thread-safe for additions.
    private readonly ConcurrentQueue<AudioCommandBase> _commandQueue = new();

    // Synchronization mechanism to control the processing loop state.
    private volatile bool _isRunning;
    private CancellationTokenSource _cts = new();
    private Task? _processingTask;

    /// <summary>
    /// Gets a value indicating whether the queue processor is currently running.
    /// </summary>
    public bool IsProcessingRunning => _isRunning;

    /// <summary>
    /// Initializes a new instance of the AudioCommandQueue, starting the background processing loop.
    /// </summary>
    // Start processing immediately upon instantiation.
    public AudioCommandQueue() => StartProcessing();


    // --- Public API: Enqueuing Commands (Thread Safe) ---

    /// <summary>
    /// Adds a command to the queue. This method is thread-safe and can be called from any thread 
    /// (e.g., the UI thread, network handler thread).
    /// </summary>
    /// <param name="command">The audio command instance to execute later.</param>
    public void EnqueueCommand(AudioCommandBase command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Add is thread-safe due to ConcurrentQueue implementation.
        _commandQueue.Enqueue(command);
        Console.WriteLine($"[QUEUE] Command '{command.GetType().Name}' enqueued successfully.");
    }

    // --- Processing Loop Management ---

    private void StartProcessing()
    {
        if (_isRunning) return;

        _cts = new CancellationTokenSource();
        _isRunning = true;
        _processingTask = Task.Run(() => ProcessCommandsLoop(_cts.Token));
        Console.WriteLine("[QUEUE] Audio Command Processor Started.");
    }

    /// <summary>
    /// The main loop that runs on the background thread, pulling commands and executing them sequentially.
    /// </summary>
    private async Task ProcessCommandsLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // 1. Check if there are any commands to process
            if (_commandQueue.TryDequeue(out var command))
            {
                try
                {
                    Console.WriteLine("\n--- [PROCESSOR] Dequeued Command ---");

                    // 2. Execute the command using its defined logic (CanExecute -> Execute)
                    // We use 'await' here if any future audio operation becomes asynchronous, 
                    // but for synchronous commands, direct execution is fine.
                    if (command.CanExecute(null)) // Pass null/default parameter for initial check
                    {
                        // Note: For simplicity, we assume Execute() completes synchronously.
                        // If Execute needs to await audio engine completion, the base command 
                        // should be updated to return Task or use async pattern.
                        command.Execute(null); // Pass null as default parameter for execution context
                    }
                    else
                    {
                        Console.WriteLine($"[PROCESSOR] WARNING: Command {command.GetType().Name} failed CanExecute check.");
                    }
                }
                catch (Exception ex)
                {
                    // Critical logging point: Catching exceptions prevents the entire queue from stopping.
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[PROCESSOR ERROR] Failed to process command {command?.GetType().Name}: {ex.Message}");
                    Console.ResetColor();
                }
            }
            else
            {
                // 3. Sleep mechanism: Wait briefly if the queue is empty.
                // This prevents the thread from spinning uselessly and consuming CPU cycles.
                await Task.Delay(50, cancellationToken);
            }
        }
        Console.WriteLine("[QUEUE] Audio Command Processor Stopped.");
    }

    // --- Cleanup (IDisposable Implementation) ---

    /// <summary>
    /// Stops the background processing thread gracefully. Should be called when the application closes.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            // 1. Signal cancellation to the background task
            _cts?.Cancel();

            // 2. Wait for the processing task to finish gracefully (with a timeout)
            try
            {
                if (_processingTask != null)
                {
                    Task.WaitAll([_processingTask], TimeSpan.FromSeconds(2));
                }
            }
            catch (AggregateException ae)
            {
                // Expected if cancellation was requested correctly.
                Console.WriteLine($"[QUEUE] Wait completed with expected exceptions: {ae.Message}");
            }
            finally
            {
                _cts?.Dispose();
                _processingTask = null;
                _isRunning = false;
            }
        }
    }
}
