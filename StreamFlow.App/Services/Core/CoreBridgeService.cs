using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using StreamFlow.Core.Helpers;

namespace StreamFlow.App.Services.Core;

/// <summary>
/// Manages the streamflow-core child process lifetime and its two-channel IPC:
///   stdin/stdout  — newline-delimited JSON commands and events
///   named pipe    — authenticated binary frame data (video preview frames)
/// </summary>
public sealed class CoreBridgeService : IHostedService, IDisposable
{
    private readonly ILogger<CoreBridgeService> _logger;

    private Process? _coreProcess;
    private Stream? _stdin;
    private CancellationTokenSource? _cts;
    private Task? _stdoutTask;
    private Task? _stderrTask;
    private Task? _pipeTask;
    private Task? _keepaliveTask;

    /// <summary>Persists core's stderr (its tracing output, including the "[diag]" performance
    /// lines) to disk — the Core Diagnostics panel shows the same text in a read-only textarea
    /// that can't be selected/copied, so this is the only way to actually get this data out of
    /// the app. Only opened when <see cref="DiagLogEnabled"/> — normal use pays no disk-I/O cost
    /// for this at all. One file per app session (timestamped), not rotated/cleaned up
    /// automatically; low enough volume (a handful of lines every ~2 seconds at most) that this
    /// isn't worth the added complexity of a rotation scheme.</summary>
    private StreamWriter? _coreLogFile;

    // ViewModels are resolved eagerly (MainWindow's field initializers) before this hosted
    // service's own StartAsync runs, so any command they send at construction time (e.g.
    // resuming a persisted source) would otherwise arrive before core's command loop is
    // actually listening and be silently dropped. Queued here and flushed once ReadyEvent
    // arrives — core writing to stdout is itself proof its stdin reader is up.
    private readonly List<CoreCommand> _pendingCommands = [];
    private readonly object _pendingLock = new();
    private bool _coreReady;

    // StreamWriter isn't safe for concurrent use — without this, two commands sent at nearly
    // the same time (e.g. a debounced Config push from a scene edit racing the keepalive loop's
    // periodic Standby, or two debounced pushes overlapping) can interleave their bytes on the
    // wire, corrupting both JSON lines. Core then fails to parse the garbled line and reports a
    // "serialization failed: expected value" error instead of silently misbehaving, which is how
    // this got noticed — but it could just as easily corrupt a command that does matter.
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // Core's watchdog exits it after 30s without a Standby command.
    private static readonly TimeSpan KeepaliveInterval = TimeSpan.FromSeconds(10);

    /// <summary>Raised on the thread-pool for each JSON event received from core.</summary>
    public event EventHandler<CoreEvent>? EventReceived;

    /// <summary>Raised on the thread-pool for each decoded video frame.</summary>
    public event EventHandler<VideoFrame>? FrameReceived;

    /// <summary>Raised on the thread-pool whenever <see cref="State"/> changes.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Raised on the thread-pool for each line the core writes to stderr (its tracing
    /// output). Temporary hook for the Core Diagnostics panel while investigating the startup
    /// CPU-spike bug — surfaces the [diag] capture-dimension logging added in capture.rs.</summary>
    public event EventHandler<string>? LogLineReceived;

    /// <summary>Current lifecycle state of the streamflow-core child process.</summary>
    public CoreState State { get; private set; } = CoreState.NotStarted;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly string CoreBinaryPath = Path.Combine(
        AppContext.BaseDirectory, "runtimes", "streamflow-core.exe");

    /// <summary>Opt-in switch for error-report investigations: persists core's stderr to a log
    /// file (see <see cref="_coreLogFile"/>) and runs core itself with --verbose for richer
    /// detail than the default warn-level output. Off by default so normal use pays zero cost
    /// for disk I/O nobody asked for — check via `StreamFlow.App.exe --diag-log`.</summary>
    private static readonly bool DiagLogEnabled =
        Environment.GetCommandLineArgs().Any(a => a.Equals("--diag-log", StringComparison.OrdinalIgnoreCase));

    public CoreBridgeService(ILogger<CoreBridgeService> logger)
    {
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(CoreBinaryPath))
        {
            _logger.LogWarning("streamflow-core not found at {Path} — capture bridge disabled", CoreBinaryPath);
            SetState(CoreState.BinaryMissing);
            return;
        }

        var token = ToBase64Url(RandomNumberGenerator.GetBytes(32));
        var pipeId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        if (DiagLogEnabled)
        {
            try
            {
                var logsDir = Path.Combine(AppDataPaths.RootFolder, "logs");
                Directory.CreateDirectory(logsDir);
                var logPath = Path.Combine(logsDir, $"core-{DateTime.Now:yyyyMMdd_HHmmss}.log");
                _coreLogFile = new StreamWriter(logPath, append: false) { AutoFlush = true };
                _logger.LogInformation("--diag-log active — core diagnostics being written to {Path}", logPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Failed to open core log file — diagnostics will only be visible live");
            }
        }

        _coreProcess = Process.Start(new ProcessStartInfo(CoreBinaryPath)
        {
            UseShellExecute = false,
            Arguments = DiagLogEnabled ? "--verbose" : "",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            // Without these, .NET decodes the redirected streams using the system's ANSI
            // codepage instead of UTF-8 — core's tracing output is UTF-8 (e.g. the "—" in its
            // own log lines), so anything outside ASCII came through as mojibake in both the
            // Core Diagnostics panel and the persisted log file.
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        }) ?? throw new InvalidOperationException("Process.Start returned null for streamflow-core");

        // Raw stream, not a StreamWriter — every command is encoded and written as one
        // complete byte array per WriteCommandAsync call (see its own comment for why:
        // StreamWriter's internal char-encoding buffer was implicated in a real corruption bug
        // for multi-megabyte single lines, like a static overlay's base64 image payload).
        _stdin = _coreProcess.StandardInput.BaseStream;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Auth must be the first thing core receives
        await WriteCommandAsync(new AuthCommand(token, pipeId));

        _stdoutTask = ReadStdoutLoopAsync(token, _cts.Token);
        _stderrTask = ReadStderrLoopAsync(_cts.Token);
        _keepaliveTask = SendKeepaliveLoopAsync(_cts.Token);

        SetState(CoreState.Running);
        _logger.LogInformation("streamflow-core started (pid {Pid})", _coreProcess.Id);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is null) return;

        // Ask core to exit on its own first — its Exit/Shutdown handler logs out of
        // VoicemeeterRemote (VBVMR_Logout) before terminating, which a hard Kill would
        // otherwise skip entirely. Voicemeeter counts logged-in API clients internally, so
        // skipping logout leaves that count incremented until Voicemeeter itself is restarted.
        // Bounded wait, since core not exiting on its own (crashed, hung) shouldn't block app
        // shutdown — Kill below still runs as the fallback either way.
        if (_coreProcess is { HasExited: false })
        {
            try
            {
                // WriteLineAsync has no cancellation/timeout of its own — if core's stdin pipe
                // buffer were ever full with nothing draining it (a hung/unresponsive core),
                // this would otherwise block forever, which in turn blocks App.OnExit's
                // synchronous wait on this whole method, leaving the app process alive with no
                // window (already torn down by the time OnExit runs) stuck in Task Manager. Every
                // await below is bounded for the same reason — nothing here may hang forever.
                await WriteCommandAsync(new ExitCommand()).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));
                await _coreProcess.WaitForExitAsync(timeoutCts.Token);
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Graceful core exit request failed or timed out"); }
        }

        await _cts.CancelAsync();

        await Task.WhenAll(
            SafeAwaitAsync(_stdoutTask, cancellationToken),
            SafeAwaitAsync(_stderrTask, cancellationToken),
            SafeAwaitAsync(_keepaliveTask, cancellationToken),
            SafeAwaitAsync(_pipeTask, cancellationToken));

        try { _coreProcess?.Kill(entireProcessTree: true); }
        catch (Exception ex) { _logger.LogDebug(ex, "Kill on shutdown"); }

        SetState(CoreState.Exited);
        _logger.LogInformation("streamflow-core stopped");

        _coreLogFile?.Dispose();
        _coreLogFile = null;
    }

    public Task SendCommandAsync(CoreCommand command)
    {
        lock (_pendingLock)
        {
            if (!_coreReady)
            {
                _pendingCommands.Add(command);
                _logger.LogDebug("Core not ready yet — queued command: {Type}", command.GetType().Name);
                return Task.CompletedTask;
            }
        }

        return WriteCommandAsync(command);
    }

    // ── Private ──────────────────────────────────────────────────────────────

    private async Task WriteCommandAsync(CoreCommand command)
    {
        var json = JsonSerializer.Serialize<CoreCommand>(command, JsonOpts);

        // AddStaticOverlay carries a base64-encoded pixel buffer that can run into the
        // megabytes for a full-resolution image — logging it in full balloons the log file
        // for no benefit, so log a summary instead of the actual command that goes over the wire.
        if (command is AddStaticOverlayCommand overlay)
            _logger.LogDebug("Sending command: add_static_overlay {SourceId} {Width}x{Height} ({Bytes} base64 chars)",
                overlay.SourceId, overlay.Width, overlay.Height, overlay.PixelsBase64.Length);
        else
            _logger.LogDebug("Sending command: {Json}", json);

        // Explicit \n (not Environment.NewLine) so the Rust line reader doesn't see \r\n.
        var bytes = Encoding.UTF8.GetBytes(json + "\n");

        await _writeLock.WaitAsync();
        try
        {
            if (_stdin is null)
            {
                _logger.LogWarning("Core stdin is null; cannot write command.");
                return;
            }
            await _stdin.WriteAsync(bytes);
            await _stdin.FlushAsync();
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to write command to core pipe: pipe is closed or closing.");
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogWarning(ex, "Failed to write command to core pipe: stream has been disposed.");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadStdoutLoopAsync(string authToken, CancellationToken ct)
    {
        var reader = _coreProcess!.StandardOutput;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break; // process exited
                if (string.IsNullOrWhiteSpace(line)) continue;

                CoreEvent? evt;
                try { evt = JsonSerializer.Deserialize<CoreEvent>(line, JsonOpts); }
                catch (JsonException ex)
                {
                    _logger.LogWarning("Unparseable stdout line: {Line} — {Error}", line, ex.Message);
                    continue;
                }

                if (evt is null) continue;

                _logger.LogDebug("Received event: {Line}", line);

                if (evt is ReadyEvent ready)
                {
                    _pipeTask = ConnectDataPipeAsync(ready.Pipe, authToken, ct);

                    // Ready is core's own signal that its command loop is actually listening —
                    // commands written to stdin any earlier than this have been observed to be
                    // silently dropped, so anything queued before now is flushed only here.
                    List<CoreCommand> queued;
                    lock (_pendingLock)
                    {
                        _coreReady = true;
                        queued = [.. _pendingCommands];
                        _pendingCommands.Clear();
                    }

                    // Dispatched to the background rather than awaited inline. This loop's own
                    // job is draining core's stdout, and both directions of this pipe have
                    // finite OS buffers — awaiting these writes here blocks this loop from
                    // reading anything else while they're in flight, including whatever core
                    // itself tries to write back in response (e.g. a CaptureStarted event right
                    // after decoding a multi-MB AddStaticOverlay image). If core's own stdout
                    // write then blocks on a full buffer nobody's draining, while this loop is
                    // blocked writing to core's stdin, that's a full bidirectional-pipe deadlock
                    // — which is exactly what was happening: a large queued image overlay
                    // reliably made every GetSources/GetAudioDevices sent right after it (by
                    // EnsureDevicesRefreshed) time out, since core's response could never arrive
                    // while this loop wasn't reading.
                    _ = Task.Run(async () =>
                    {
                        foreach (var command in queued)
                            await WriteCommandAsync(command);
                    }, ct);
                }

                EventReceived?.Invoke(this, evt);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogError(ex, "stdout reader faulted"); }
        finally
        {
            if (!ct.IsCancellationRequested)
                SetState(CoreState.Exited);
        }
    }

    private async Task ReadStderrLoopAsync(CancellationToken ct)
    {
        var reader = _coreProcess!.StandardError;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                _logger.LogWarning("streamflow-core stderr: {Line}", line);
                LogLineReceived?.Invoke(this, line);
                try
                {
                    _coreLogFile?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");
                }
                catch (IOException) { }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogError(ex, "stderr reader faulted"); }
    }

    private async Task SendKeepaliveLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(KeepaliveInterval);
            while (await timer.WaitForNextTickAsync(ct))
                await WriteCommandAsync(new StandbyCommand());
        }
        catch (OperationCanceledException) { }
    }

    private void SetState(CoreState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ConnectDataPipeAsync(string pipeName, string authToken, CancellationToken ct)
    {
        // Core may send the full \\.\pipe\ path or just the local name
        const string prefix = @"\\.\pipe\";
        var localName = pipeName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? pipeName[prefix.Length..] : pipeName;

        await using var pipe = new NamedPipeClientStream(
            ".", localName, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync(5_000, ct);
            _logger.LogInformation("Data pipe connected: {Pipe}", localName);

            // Handshake: prove identity before frames start
            var hello = JsonSerializer.Serialize<CoreCommand>(new HelloCommand(authToken), JsonOpts) + "\n";
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(hello), ct);
            await pipe.FlushAsync(ct);

            // Binary frame loop
            var header = new byte[8];
            var frameCount = 0;
            while (!ct.IsCancellationRequested)
            {
                await ReadExactAsync(pipe, header, ct);

                var frameType = BitConverter.ToUInt32(header, 0);
                var payloadLen = (int)BitConverter.ToUInt32(header, 4);

                var payload = new byte[payloadLen];
                await ReadExactAsync(pipe, payload, ct);

                if (frameType == 1) // VideoPreview
                {
                    // Actual wire layout (native/crates/core/src/main.rs run_data_pipe):
                    // u8 source_id_len, [source_id_bytes], u32 width, u32 height, BGRA pixels.
                    // This differs from streamflow-ipc's doc comment, which is stale.
                    var sourceIdLen = payload[0];
                    var sourceId = Encoding.UTF8.GetString(payload, 1, sourceIdLen);
                    var dimsOffset = 1 + sourceIdLen;
                    var w = (int)BitConverter.ToUInt32(payload, dimsOffset);
                    var h = (int)BitConverter.ToUInt32(payload, dimsOffset + 4);
                    // Core sends BGRA already — matches WPF's Bgra32 format directly, no
                    // channel-swap needed.
                    var pixels = payload[(dimsOffset + 8)..];

                    if (frameCount++ % 30 == 0)
                        _logger.LogDebug("Received preview frame #{Count}: {SourceId} {W}x{H}", frameCount, sourceId, w, h);
                    FrameReceived?.Invoke(this, new VideoFrame(sourceId, w, h, pixels));
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogError(ex, "Data pipe faulted"); }
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0) throw new EndOfStreamException("Data pipe closed mid-frame");
            offset += read;
        }
    }

    private static async Task SafeAwaitAsync(Task? task, CancellationToken ct)
    {
        if (task is null) return;
        try { await task.WaitAsync(TimeSpan.FromSeconds(3), ct); }
        catch { }
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public void Dispose()
    {
        _cts?.Dispose();
        _stdin?.Dispose();
        _coreProcess?.Dispose();
        _writeLock.Dispose();
        _coreLogFile?.Dispose();
    }
}
