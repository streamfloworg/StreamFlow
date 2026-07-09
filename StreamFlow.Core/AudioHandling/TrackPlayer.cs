using System.Diagnostics;
using System.IO;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Windows.Shapes;
using System.Windows.Threading;


using libxmpBindings;

using StreamFlow.Core.AudioProperties;
using StreamFlow.Core.Data;

using SoundFlow.Abstracts;
using SoundFlow.Components;
using SoundFlow.Exceptions;
using SoundFlow.Interfaces;
using SoundFlow.Providers;
using SoundFlow.Structs;
using SoundFlow.Enums;

using SoundFlowPlaybackState = SoundFlow.Enums.PlaybackState;

using Logger = StreamFlow.Core.Helpers.LoggerService;

namespace StreamFlow.Core.AudioHandling;

/// <summary>
/// Provides functionality for managing audio playback, including loading, playing, stopping,  seeking, and retrieving
/// playback-related information.
/// </summary>
/// <remarks>The <see cref="TrackPlayer"/> class is a static utility for handling audio tracks. It supports 
/// operations such as loading audio files, controlling playback, and managing playback state.  This class also provides
/// event-based notifications for state changes and exception handling  during playback or loading operations. <para>
/// The class is designed to be used in scenarios where audio playback is required, such as  media players or audio
/// processing applications. It includes features like visualization data  generation, duration calculation, and exception
/// management. </para> <para> Thread safety is not guaranteed for all members. Ensure proper synchronization when
/// accessing  shared resources in a multithreaded environment. </para></remarks>
[DebuggerDisplay("(TrackPlayer) {Name,np} - Volume: {System.Math.Round(Volume * 100),np} - Position: {Position} / Duration: {Duration}")]
public partial class TrackPlayer
{
    // Create a Subject as the single source of events
    private static readonly Subject<TrackPlayerEventArgs> _trackPlayerSubject = new();

    // Expose as IObservable for external subscription
    private static readonly IObservable<TrackPlayerEventArgs> TrackPlayerEvents = _trackPlayerSubject.AsObservable();

    // Optional: Create a multicast observable for hot sharing
    public static IObservable<TrackPlayerEventArgs> MultiCast
    {
        get; private set;
    }

    static TrackPlayer()
    {
        StatusContext = SynchronizationContext.Current ?? throw new InvalidOperationException("This class must not be instantiated on the UI thread");
        SynchronizationContext.SetSynchronizationContext(StatusContext);

        MultiCast = TrackPlayerEvents.Replay(1).RefCount(1);                // Buffer the last event for new subscribers
                                                                            // and maintain connection as long as
                                                                            // at least one subscriber exists
        MultiCast
            // .Where( << Insert criteria here >> )                         // Filter unwanted events
            .DistinctUntilChanged(e => new { e.Status, e.PlaybackState })   // Only emit when state actually changes
            .ObserveOn(StatusContext);                                      // Ensure UI thread delivery
    }

    public static SoundPlayer? PlayerRouter
    {
        get;
        private set;
    }

    public static bool HasPlayer() { return PlayerRouter != null; }

    // Keep XMP instance alive during playback for module files
    private static Xmp? _currentXmpInstance;

    // Background task for streaming XMP audio data to QueueDataProvider
    private static Task? _xmpStreamingTask;
    private static CancellationTokenSource? _xmpStreamingCts;

    // Diagnostic: Track wall-clock time vs playback position
    private static DateTime _playbackStartTime;
    private static TimeSpan _playbackStartPosition;

    private static AudioTrack _audioTrack = NullAudio.NullTrack;

    public static AudioTrack AudioTrack
    {
        get => _audioTrack;
        private set => _audioTrack = value;
    }

    public static AudioFormat AudioFormat
    {
        get; private set;
    }

    public delegate void TrackPlayerEventHandler(object sender, TrackPlayerEventArgs e);

    /// <summary>
    /// Gets or sets a value indicating whether exceptions should be cleared after they are retrieved.
    /// </summary>
    /// <remarks>When set to <see langword="true"/>, any exceptions retrieved will be automatically cleared
    /// from the internal storage.  This can be useful in scenarios where exceptions are processed and should not
    /// persist beyond their retrieval.</remarks>
    public static bool ClearExceptionsOnRetrieval { get; set; } = false;
    public static bool ClearSubscriptionsOnStop { get; set; } = false;

    /// <summary>
    /// Gets or sets the event handler that is invoked when the track player state changes.
    /// </summary>
    private static List<Exception> Exceptions { get; set; } = [];

    private static SynchronizationContext? StatusContext { get; set; }

    private static PlaybackState _playbackState = PlaybackState.Stopped;

    public static PlaybackState PlaybackState
    {
        get => _playbackState;
        private set => _playbackState = value;
    }

    /// <summary>
    /// Longer audio with repeat and fade out
    /// </summary>
    //[DebuggerDisplay("Track Player Status: {Status,np} - Track Player Playback State: {PlaybackStatus,np}")]
    public static Statuses Status { get; private set; } = Statuses.Reset;
    //public static Statuses Status { get; private set; } = Statuses.Empty;

    private static Statuses previousStatus = Statuses.Ready;
    public static bool IsScrubbing
    {
        get; private set;
    }

    /// <summary>
    /// Sets the playback status to "Scrubbing" and updates the playback state accordingly.
    /// </summary>
    /// <remarks>This method changes the current playback status to indicate that scrubbing is in progress. 
    /// It also updates the playback state based on the current state of the player router.  After updating the status
    /// and state, it raises the <see cref="TrackPlayerChanged"/> event  with the updated values and any associated
    /// exceptions.</remarks>
    public static void SetScrubbing()
    {
        Logger.DebugLog(nameof(TrackPlayer), "Entering Scrubbing state");
        previousStatus = Status;
        Status = Statuses.Scrubbing;
        EmitStateChange();
    }

    /// <summary>
    /// Resets the scrubbing state of the track player to its previous status.
    /// </summary>
    /// <remarks>This method restores the playback status and state to their values prior to scrubbing.  It
    /// also triggers the <see cref="TrackPlayerChanged"/> event to notify subscribers of the updated state.</remarks>
    public static void UnsetScrubbing()
    {
        Logger.DebugLog(nameof(TrackPlayer), "Exiting Scrubbing state");
        Status = previousStatus;
        EmitStateChange();
    }

    private static void EmitStateChange(TrackPlayerEventArgs args = null, [CallerLineNumber] int line = 0, [CallerMemberName] string name = null)
    {
        //Logger.DebugLog(nameof(TrackPlayer), $"Emitting State Change: {args ?? new TrackPlayerEventArgs(Status, PlaybackState, GetExceptions())}");
        //Logger.DebugLog(nameof(TrackPlayer), $"  at {name} (line {line})");
        args ??= new TrackPlayerEventArgs(Status, PlaybackState, GetExceptions());
        _trackPlayerSubject.OnNext(args); // This triggers ALL subscribers
    }
        
    /// <summary>
    /// Attempts to start playback using the current player router.
    /// </summary>
    /// <remarks>This method updates the playback status and state based on the result of the operation.  If
    /// playback is successful, the <see cref="PlaybackState"/> is updated to reflect the current state of the player.
    /// Any exceptions encountered during the playback attempt are added to the <see cref="PlaybackExceptions"/>
    /// collection.</remarks>
    /// <returns><see langword="true"/> if playback starts successfully; otherwise, <see langword="false"/>.</returns>
     public static void Play()
    {
        Logger.InfoLog(nameof(TrackPlayer), $"═══ Play() called ═══");
        Logger.InfoLog(nameof(TrackPlayer), $"  PlayerRouter: {(PlayerRouter != null ? "EXISTS" : "NULL")}");
        Logger.InfoLog(nameof(TrackPlayer), $"  AudioTrack: {AudioTrack?.Name}");

        if (PlayerRouter is not null)
        {
            try
            {
                Logger.DebugLog(nameof(TrackPlayer), $"Setting volume to {AudioTrack.Volume}");
                PlayerRouter.Volume = (float)AudioTrack.Volume;

                Logger.InfoLog(nameof(TrackPlayer), "Calling PlayerRouter.Play()...");
                PlayerRouter.Play();

                _playbackStartTime = DateTime.Now.ToLocalTime();
                _playbackStartPosition = PlayerRouter.DataProvider.Position > 0 
                    ? TimeSpan.FromSeconds((float)PlayerRouter.DataProvider.Position / AudioEngine.GetAudioFormat().Channels / AudioEngine.GetAudioFormat().SampleRate)
                    : TimeSpan.Zero;
                Logger.InfoLog(nameof(TrackPlayer), $"⏱️ Playback timer started at {_playbackStartTime:HH:mm:ss.fff}, position: {_playbackStartPosition:mm\\:ss\\.fff}");

                // Always update state and emit change, even if Play() is called before
                // the PlayerRouter has fully transitioned to Playing state
                if (Status != Statuses.Scrubbing)
                {
                    PlaybackState = PlayerRouter.State.ToCore();
                    Status = Statuses.Ready;
                    Logger.InfoLog(nameof(TrackPlayer), $"{PlaybackState}: '{AudioTrack?.Name}' - Status: {Status}");
                }
                // Always emit state change so subscribers get notified
                EmitStateChange();
                Logger.InfoLog(nameof(TrackPlayer), "═══ Play() completed successfully ═══");
            }
            catch (Exception ex)
            { 
                Exceptions.Add(ex);
                Logger.ErrorLog(nameof(TrackPlayer), $"✗ Exception in Play(): {ex.Message}");
                Logger.ErrorLog(nameof(TrackPlayer), $"  Stack trace: {ex.StackTrace}");
            }

            if (Exceptions.Count > 0)
            {
                Logger.ErrorLog(nameof(TrackPlayer), $"Error starting '{AudioTrack?.Name}' - Exceptions: {Exceptions.Count}");
                foreach (var ex in Exceptions)
                {
                    Logger.ErrorLog(nameof(TrackPlayer), $"Exception: {ex}");
                }
            }
        }
        else
        {
            Logger.ErrorLog(nameof(TrackPlayer), "✗ Cannot play - PlayerRouter is NULL!");
        }
    }

    /// <summary>
    /// Pauses the current playback if a player is available.
    /// </summary>
    /// <remarks>This method attempts to pause the playback using the current player. If the player is
    /// unavailable  or an error occurs during the operation, the method will return <see langword="false"/>.  If the
    /// operation is successful, the playback state is updated to <see cref="PlaybackState.Paused"/>  and the status is
    /// set to <see cref="Statuses.Busy"/>.</remarks>
    /// <returns><see langword="true"/> if the playback was successfully paused; otherwise, <see langword="false"/>.</returns>
    public static void Pause()
    {
        try
        {
            if (PlayerRouter is not null)
            {

                PlayerRouter.Pause();
                if (PlayerRouter.State == SoundFlowPlaybackState.Paused && Status != Statuses.Scrubbing)
                {
                    PlaybackState = PlayerRouter.State.ToCore();
                    Status = Statuses.Ready;
                    Logger.InfoLog(nameof(TrackPlayer), $"{PlaybackState} -> '{AudioTrack?.Name}' - Status: {Status}");
                    EmitStateChange();
                }
            }
        }
        catch (Exception ex)
        {
            Exceptions.Add(ex);
        }
        if (Exceptions.Count > 0)
        {
            Logger.ErrorLog(nameof(TrackPlayer), $"Error: {PlaybackState} -> '{AudioTrack?.Name}' - Exceptions: {Exceptions.Count}");
        }
    }

    /// <summary>
    /// Stops the playback and optionally unloads the current track.
    /// </summary>
    /// <remarks>This method stops the playback by invoking the underlying player router's stop functionality.
    /// The method updates the playback status and raises the <see cref="TrackPlayerChanged"/> event  to notify
    /// listeners of the state change. Any exceptions encountered during the stop operation  are added to the
    /// playback exceptions collection.</remarks>
    /// <returns><see langword="true"/> if playback was successfully stopped; otherwise, <see langword="false"/>.</returns>
    public static bool Stop(Statuses status = Statuses.Reset)
    {
        var successful = false;
        try
        {
            PlayerRouter!.PlaybackEnded -= PlayerRouter_PlaybackEnded;
            PlayerRouter?.Stop();

            // Properly dispose of PlayerRouter to free audio resources
            if (PlayerRouter != null)
            {
                try
                {
                    PlayerRouter.Dispose();
                }
                catch (Exception disposeEx)
                {
                    Logger.ErrorLog(nameof(TrackPlayer), $"Error disposing PlayerRouter: {disposeEx.Message}");
                }
                PlayerRouter = null;
            }

            // Stop XMP streaming task if running
            if (_xmpStreamingTask != null)
            {
                try
                {
                    Logger.DebugLog(nameof(TrackPlayer), "Cancelling XMP streaming task");
                    _xmpStreamingCts?.Cancel();

                    // Wait for task to complete (with timeout)
                    if (!_xmpStreamingTask.Wait(TimeSpan.FromSeconds(5)))
                    {
                        Logger.ErrorLog(nameof(TrackPlayer), "XMP streaming task did not complete within timeout");
                    }
                }
                catch (Exception streamingEx)
                {
                    Logger.ErrorLog(nameof(TrackPlayer), $"Error stopping XMP streaming task: {streamingEx.Message}");
                }
                finally
                {
                    _xmpStreamingTask = null;
                    _xmpStreamingCts?.Dispose();
                    _xmpStreamingCts = null;
                }
            }

            // Dispose XMP instance if it exists
            if (_currentXmpInstance != null)
            {
                try
                {
                    Logger.DebugLog(nameof(TrackPlayer), "Disposing XMP instance");
                    _currentXmpInstance.Dispose();
                }
                catch (Exception xmpDisposeEx)
                {
                    Logger.ErrorLog(nameof(TrackPlayer), $"Error disposing XMP instance: {xmpDisposeEx.Message}");
                }
                _currentXmpInstance = null;
            }

            Exceptions.Clear();
            if (PlayerRouter?.State == SoundFlowPlaybackState.Stopped || PlayerRouter == null)
            {
                Logger.InfoLog(nameof(TrackPlayer), $"{PlaybackState.Stopped} -> '{AudioTrack?.Name}' - {Status}");
                AudioTrack = NullAudio.NullTrack;
                Duration = TimeSpan.Zero;
                Status = status;
                PlaybackState = PlaybackState.Stopped;
                successful = true;
            }
        }
        catch (Exception ex)
        {
            Exceptions.Add(ex);
        }
        if (Exceptions.Count > 0)
        {
            Logger.ErrorLog(nameof(TrackPlayer), $"Error: {PlaybackState} -> '{AudioTrack?.Name}' - Exceptions: {Exceptions.Count}");
        }
        EmitStateChange();
        return successful;
    }

    /// <summary>
    /// Retrieves a dictionary of categorized exceptions that have occurred during loading and playback operations.
    /// </summary>
    /// <remarks>The returned dictionary contains two entries: <list Type="bullet"> <item>
    /// <term><c>LoadingExceptions</c></term> <description>A list of exceptions that occurred during loading
    /// operations.</description> </item> <item> <term><c>PlaybackExceptions</c></term> <description>A list of
    /// exceptions that occurred during playback operations.</description> </item> </list> If <see
   /// cref="ClearExceptionsOnRetrieval"/> is set to <see langword="true"/>, the exceptions will be cleared after
    /// retrieval.</remarks>
    /// <returns>A dictionary where the keys are the exception categories (<c>LoadingExceptions</c> and
    /// <c>PlaybackExceptions</c>)  and the values are lists of exceptions associated with each category.</returns>
    private static List<Exception> GetExceptions()
    {
        static List<Exception> ReturnExceptions()
        {
            return Exceptions;
        }

        var exceptions = ReturnExceptions();
        if (ClearExceptionsOnRetrieval)
        {
            Exceptions.Clear();
        }

        return exceptions;
    }

    public static Task LoadAudioTask(List<AudioAnalyzer> analyzers = default,
        Action<int>? progressCallback = null,
        CancellationToken ct = new())
    {
        try
        {
            if (!AudioEngine.Instance.CurrentPlaybackDevice!.Capability.HasFlag(Capability.Playback))
            {
                AudioEngine.Instance.CurrentPlaybackDevice.Dispose();
                throw new InvalidOperationException("Current playback device does not support playback capability.");
            }

            if (AudioEngine.Instance.CurrentPlaybackDevice?.IsRunning == true)
            {
                AudioEngine.Instance.CurrentPlaybackDevice?.Stop();
            }

            return LoadAudioStream(analyzers, progressCallback, ct);
        }
        catch (Exception ex)
        {
            Status = Statuses.Error;
            Exceptions.Add(ex);
            EmitStateChange();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Loads the specified audio track into the TrackPlayer.
    /// </summary>
    /// <param name="track">The audio track to be loaded. Cannot be null.</param>
    /// <param name="analyzers">List of audio analyzers to attach</param>
    /// <param name="progressCallback">Optional progress callback for tracker files (0-100)</param>
    /// <param name="ct">Cancellation token</param>
    public static async Task LoadAudio(
        AudioTrack track, 
        List<AudioAnalyzer> analyzers = default, 
        Action<int>? progressCallback = null,
        CancellationToken ct = new())
    {
        //var previousStatus = Status;
        //var previousPlaybackState = PlaybackState;
        AudioTrack = track;
        await LoadAudioTask(analyzers, progressCallback, ct);
        if (Exceptions.Count > 0)
        {
            Logger.ErrorLog(nameof(TrackPlayer), $"Error: {PlaybackState} -> '{AudioTrack?.Name}' - Exceptions: {Exceptions.Count}");
            EmitStateChange();
        }
    }

    /// <summary>
    /// Loads the audio stream from the current audio track and initializes the audio player.
    /// </summary>
    /// <remarks>This method attempts to load the audio file specified by the current <see cref="AudioTrack"/>
    /// and create an audio player for playback. If the audio file path is invalid or an error occurs during loading,
    /// the method returns <see langword="false"/> and the exception is added to <see
    /// cref="LoadingExceptions"/>.</remarks>
    /// <returns><see langword="true"/> if the audio stream is successfully loaded and the audio player is initialized;
    /// otherwise, <see langword="false"/>.</returns>
    private static async Task LoadAudioStream(
        List<AudioAnalyzer> analyzers, 
        Action<int>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var successful = false;

        async Task LoadAudio()
        {
            if (AudioTrack is not null && AudioTrack.ValidPath)
            {
                // Create a new FileStream that will be owned by AssetDataProvider
                // The provider/decoder will handle disposal
                FileStream audioStream = null;

                try
                {
                    // Check if this is a tracker file
                    // IMPORTANT: Use .ToLower() for case-insensitive extension matching
                    var isTrackerFormat = FileExtension.EndsWith(AppModel.Instance.ValidModuleExtensions, AudioTrack.FilePath.ToLower());

                    ISoundDataProvider dataProvider;
                    AudioFormatInfo? moduleFormatInfo = null; // Store module format info for device configuration

                    if (isTrackerFormat)
                    {
                        // Close the file stream - XMP will open it directly
                        //audioStream.Dispose();
                        Logger.InfoLog(nameof(TrackPlayer), $"🔍 BEFORE MODULE LOAD: AudioEngine.AudioFormat.SampleRate = {AudioEngine.GetAudioFormat().SampleRate}Hz");

                        Logger.InfoLog(nameof(TrackPlayer), $"═══ Starting XMP Module Load ═══");
                        Logger.InfoLog(nameof(TrackPlayer), $"  File: {AudioTrack.FilePath}");

                        // Test module first
                        if (!Xmp.TestModule(AudioTrack.FilePath, out var testInfo))
                        {
                            Logger.ErrorLog(nameof(TrackPlayer), $"✗ Module test failed: {AudioTrack.FilePath}");
                            throw new InvalidOperationException($"File is not a valid tracker module: {AudioTrack.FilePath}");
                        }
                        

                        Logger.InfoLog(nameof(TrackPlayer), $"✓ Module test passed:");
                        Logger.InfoLog(nameof(TrackPlayer), $"  Name: {testInfo.Name}");
                        Logger.InfoLog(nameof(TrackPlayer), $"  Format: {testInfo.Format}");

                        // Dispose old XMP instance if exists
                        if (_currentXmpInstance != null)
                        {
                            Logger.DebugLog(nameof(TrackPlayer), "Disposing previous XMP instance");
                            try
                            {
                                _currentXmpInstance.Dispose();
                            }
                            catch (Exception xmpDisposeEx)
                            {
                                Logger.ErrorLog(nameof(TrackPlayer), $"Error disposing old XMP instance: {xmpDisposeEx.Message}");
                            }
                            _currentXmpInstance = null;
                        }

                        // Create XMP instance and keep it alive
                        // IMPORTANT: Use standard tracker sample rate (44100Hz) for accurate playback speed
                        // XMP modules are typically rendered at 44100Hz, NOT the engine's sample rate

                        // Get audio format info
                        Logger.DebugLog(nameof(TrackPlayer), "Getting audio format info...");
                        moduleFormatInfo = new Xmp()?.GetAudioFormatFromFile(AudioTrack.FilePath);
                        if (moduleFormatInfo == null)
                        {
                            _currentXmpInstance?.Dispose();
                            _currentXmpInstance = null;
                            Logger.ErrorLog(nameof(TrackPlayer), $"✗ Failed to get format info: {AudioTrack.FilePath}");
                            throw new InvalidOperationException($"Failed to get audio format from module: {AudioTrack.FilePath}");
                        }
                        Logger.InfoLog(nameof(TrackPlayer), $"✓ Format info:");
                        Logger.InfoLog(nameof(TrackPlayer), $"   Rate: {moduleFormatInfo.SampleRate}Hz, Channels: {moduleFormatInfo.Channels}, Duration: {moduleFormatInfo.EstimatedDuration:mm\\:ss}");


                        Logger.DebugLog(nameof(TrackPlayer), $"Creating XMP instance (SampleRate: {moduleFormatInfo.SampleRate})");
                        _currentXmpInstance = new Xmp(rate: moduleFormatInfo.SampleRate, format: moduleFormatInfo.Format);

                        Logger.DebugLog(nameof(TrackPlayer), "Loading module into XMP...");
                        if (!_currentXmpInstance.LoadModule(AudioTrack.FilePath))
                        {
                            _currentXmpInstance.Dispose();
                            _currentXmpInstance = null;
                            Logger.ErrorLog(nameof(TrackPlayer), $"✗ XMP LoadModule failed: {AudioTrack.FilePath}");
                            throw new InvalidOperationException($"Failed to load tracker module: {AudioTrack.FilePath}");
                        }
                        Logger.InfoLog(nameof(TrackPlayer), "✓ Module loaded successfully");

                        // Open audio stream
                        Logger.DebugLog(nameof(TrackPlayer), "Opening audio stream...");
                        // TEST: Enable looping to verify if module has internal loop point
                        var xmpStream = _currentXmpInstance.OpenAudioStream(loop: true, bufferSize: 8192);
                        if (xmpStream == null)
                        {
                            _currentXmpInstance.Dispose();
                            _currentXmpInstance = null;
                            Logger.ErrorLog(nameof(TrackPlayer), $"✗ Failed to create audio stream: {AudioTrack.FilePath}");
                            throw new InvalidOperationException($"Failed to create audio stream from module: {AudioTrack.FilePath}");
                        }
                        Logger.InfoLog(nameof(TrackPlayer), "✓ Audio stream created");

                        var actualBitsPerSample = moduleFormatInfo.SampleRate * moduleFormatInfo.Channels * moduleFormatInfo.EstimatedDuration.Value.TotalSeconds / moduleFormatInfo.SampleRate;
                        if (actualBitsPerSample != moduleFormatInfo.BitsPerSample)
                        {
                            Logger.ErrorLog(nameof(TrackPlayer), $"⚠️ Bit depth mismatch! XMP:{moduleFormatInfo.BitsPerSample}-bit, Actual:{actualBitsPerSample}-bit");
                        }

                        var sampleFormat = actualBitsPerSample == 8 ? SampleFormat.U8 : SampleFormat.S16;

                        Logger.InfoLog(nameof(TrackPlayer), $"📊 DATA PROVIDER CREATED:");
                        Logger.InfoLog(nameof(TrackPlayer), $"   Format: {moduleFormatInfo.Format}, Rate: {moduleFormatInfo.SampleRate}Hz");
                        Logger.InfoLog(nameof(TrackPlayer), $"   XMP Channels: {moduleFormatInfo.Channels}, Engine Channels: {AudioEngine.GetAudioFormat().Channels}");

                        Logger.InfoLog(nameof(TrackPlayer), $"═══ Config: {moduleFormatInfo.SampleRate}Hz, {moduleFormatInfo.Channels}ch, {moduleFormatInfo.Format} → Engine: {AudioEngine.GetAudioFormat().SampleRate}Hz ═══");

                        // Start background task to stream XMP audio data to the queue provider
                        _xmpStreamingCts?.Cancel();
                        _xmpStreamingCts?.Dispose();
                        _xmpStreamingCts = new CancellationTokenSource();

                        // Step 3: Pre-render entire module to memory
                        Logger.InfoLog(nameof(TrackPlayer), "🎵 Started XMP audio streaming task");

                        _currentXmpInstance.StartPlayer();

                        using var memoryStream = new MemoryStream();
                        var renderBuffer = new byte[8192];

                        while (_currentXmpInstance.ReadBuffer(renderBuffer, false) && !_xmpStreamingCts.IsCancellationRequested)
                        {
                            memoryStream.Write(renderBuffer, 0, renderBuffer.Length);
                        }

                        _currentXmpInstance.EndPlayer();
                        _currentXmpInstance.Dispose();
                        _currentXmpInstance = null;

                        dataProvider = new RawDataProvider(memoryStream.ToArray(), SampleFormat.S16, moduleFormatInfo.SampleRate);
                    }
                    else
                    {
                        // Use standard provider for other formats
                        audioStream = new FileStream(AudioTrack.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        dataProvider = new AssetDataProvider(AudioEngine.Engine, AudioEngine.GetAudioFormat(), audioStream);
                    }

                    //var buffer = new Span<float>();
                    //dataProvider.ReadBytes(buffer);
                    //VisualizationData = buffer.ToArray();
                    if (null != dataProvider)
                    {
                        // Wrap data provider to intercept raw PCM read requests and feed them to the WaveformAnalyzer (pre-volume/pre-fader)
                        if (AudioEngine.WaveformAnalyzer != null)
                        {
                            dataProvider = new VisualizingSoundDataProvider(dataProvider, AudioEngine.WaveformAnalyzer);
                        }

                        // For tracker modules, only reconfigure if sample rate is different
                        if (isTrackerFormat && moduleFormatInfo != null)
                        {
                            if (moduleFormatInfo.SampleRate != AudioEngine.GetAudioFormat().SampleRate)
                            {
                                Logger.InfoLog(nameof(TrackPlayer), $"🔧 Reconfiguring: {AudioEngine.GetAudioFormat().SampleRate}Hz → {moduleFormatInfo.SampleRate}Hz");
                                AudioEngine.SetPlaybackDevice(sampleRate: moduleFormatInfo.SampleRate);

                                var actualDeviceRate = AudioEngine.Instance.CurrentPlaybackDevice?.Format.SampleRate ?? 0;
                                if (actualDeviceRate != moduleFormatInfo.SampleRate)
                                {
                                    Logger.ErrorLog(nameof(TrackPlayer), $"⚠️ Device rate mismatch! Expected:{moduleFormatInfo.SampleRate}Hz, Actual:{actualDeviceRate}Hz");
                                }
                            }
                            else
                            {
                                Logger.InfoLog(nameof(TrackPlayer), $"ℹ️ Device already at correct rate ({moduleFormatInfo.SampleRate}Hz), no reconfiguration needed");
                            }
                        }

                        SoundPlayer tempSoundPlayer = new(AudioEngine.Engine, AudioEngine.GetAudioFormat(), dataProvider)
                        {
                            Name = AudioTrack.Name
                        };

                        Logger.InfoLog(nameof(TrackPlayer), $"⏱️ SoundPlayer: Duration={tempSoundPlayer.Duration:F1}s, Length={tempSoundPlayer.DataProvider.Length} samples");

                        if (analyzers.Count > 0)
                        {
                            foreach (var analyzer in analyzers)
                            {
                                // Waveform analyzer is already processed pre-volume via VisualizingSoundDataProvider wrapper
                                if (analyzer is RealtimeWaveformAnalyzer)
                                {
                                    continue;
                                }

                                if (tempSoundPlayer.Analyzers.Any(x => x.Name == analyzer.Name) == false)
                                {
                                    tempSoundPlayer.AddAnalyzer(analyzer);
                                    Logger.InfoLog(nameof(TrackPlayer), $"Added analyzer: {analyzer.Name}");
                                }
                                else
                                {
                                    tempSoundPlayer.RemoveAnalyzer(tempSoundPlayer.Analyzers.First(x => x.Name == analyzer.Name));
                                    Logger.InfoLog(nameof(TrackPlayer), $"Removed analyzer with duplicate name: {analyzer.Name}");
                                    tempSoundPlayer.AddAnalyzer(analyzer);
                                    Logger.InfoLog(nameof(TrackPlayer), $"Added replacement analyzer: {analyzer.Name}");
                                }
                            }
                        }
                        Duration = TimeSpan.FromSeconds(tempSoundPlayer.Duration);
                        if (PlayerRouter is not null)
                        {
                            PlayerRouter.PlaybackEnded -= PlayerRouter_PlaybackEnded;
                            // Clear out any existing playback device component, just in case
                            if (AudioEngine.Instance.CurrentPlaybackDevice?.IsRunning == true)
                            {
                                AudioEngine.Instance.CurrentPlaybackDevice?.Stop();
                            }
                            if (
                                    AudioEngine.Instance.CurrentPlaybackDevice?.MasterMixer.Components.Count > 0 &&
                                    AudioEngine.Instance.CurrentPlaybackDevice?.MasterMixer.Components.FirstOrDefault
                                    (
                                        x => x.GetType() == typeof(SoundPlayer) &&
                                        x.Name == PlayerRouter.Name
                                    ) is SoundPlayer player
                                )
                            {
                                AudioEngine.Instance.CurrentPlaybackDevice?.MasterMixer.RemoveComponent(player);
                            }

                            // CRITICAL FIX: Dispose old PlayerRouter to free resources before replacing
                            try
                            {
                                PlayerRouter.Dispose();
                                Logger.DebugLog(nameof(TrackPlayer), "Disposed old PlayerRouter");
                            }
                            catch (Exception disposeEx)
                            {
                                Logger.ErrorLog(nameof(TrackPlayer), $"Error disposing old PlayerRouter: {disposeEx.Message}");
                                Exceptions.Add(disposeEx);
                            }
                        }
                        if (tempSoundPlayer is not null)
                        {
                            try
                            {
                                PlayerRouter = tempSoundPlayer;
                                PlayerRouter!.SetTimeStretchQuality(WsolaPerformancePreset.Balanced);
                                PlayerRouter.PlaybackEnded += PlayerRouter_PlaybackEnded;
                                AudioEngine.Instance.CurrentPlaybackDevice?.MasterMixer.AddComponent(PlayerRouter);
                                AudioEngine.Instance.CurrentPlaybackDevice?.Start();
                                successful = true;
                            }
                            catch (BackendException beEx)
                            {
                                Exceptions.Add(beEx);

                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // If we fail after creating the stream but before transferring ownership
                    // ensure the stream is disposed
                    audioStream?.Dispose();
                    Exceptions.Add(ex);
                    Logger.ErrorLog(nameof(TrackPlayer), $"Error loading audio stream: {ex.Message}");
                }
            }
            if (successful)
            {
                Status = Statuses.Loaded;
                Logger.InfoLog(nameof(TrackPlayer), $"🎵 Audio Analysis Complete: {ToTrackString()} 🎵");
            }
            else
            {
                Status = Statuses.Error;
                Logger.InfoLog(nameof(TrackPlayer), $"🎵 Audio Analysis Failed: {ToTrackString()} 🎵");
            }
            EmitStateChange();
        }

        Status = Statuses.Loading;
        EmitStateChange();

        // Execute LoadAudio directly on background thread (Task.Run context)
        // No need to marshal to UI thread - file I/O, MOD rendering, and audio
        // component creation can all happen on background threads
        try
        {
            await LoadAudio();
        }
        catch (Exception ex)
        {
            Exceptions.Add(ex);
            successful = false;
        }
        if (successful)
        {
            Status = Statuses.Ready;
            PlaybackState = PlayerRouter!.State.ToCore();
        }
        EmitStateChange();
    }

    /// <summary>
    /// Handles the event triggered when playback ends.
    /// </summary>
    /// <param name="sender">The source of the event. This can be <see langword="null"/>.</param>
    /// <param name="e">An <see cref="EventArgs"/> instance containing the event data.</param>
    private static void PlayerRouter_PlaybackEnded(object? sender, EventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            Stop(Statuses.PlaybackEnded);
        }));
    }

    /// <summary>
    /// Seeks to a given position in the audio stream
    /// </summary>
    /// <param name="position"></param>
    /// <returns>True is Seek was successful, otherwise false</returns>
    public static bool Seek(TimeSpan seekPosition)
    {

        var result = PlayerRouter != null ? (bool)PlayerRouter?.Seek(seekPosition) : false;
        if (result)
        {
            if (Status != Statuses.Scrubbing)
            {
                //Status = Statuses.Ready;
                EmitStateChange();
            }
        }
        return result;
    }

    /// <summary>
    /// Gets the current playback position of the media, expressed as a <see cref="TimeSpan"/>.
    /// </summary>
    public static TimeSpan Position => TimeSpan.FromSeconds(Convert.ToDouble(PlayerRouter?.Time ?? 0));

    /// <summary>
    /// Gets the duration of the operation.
    /// </summary>
    public static TimeSpan Duration
    {
        get;
        private set;
    }

    /// <summary>
    /// Gets the visualization data represented as an array of floating-point values.
    /// </summary>
    public static float[] VisualizationData
    {
        get;
        private set;
    } = [];

    /// <summary>
    /// Gets the collection of loop points associated with the current audio track.
    /// </summary>
    public static List<LoopPoint> LoopPoints => AudioTrack?.LoopPoints ?? [];

    /// <summary>
    /// Gets or sets the collection of active subscriptions.
    /// </summary>
    private static List<Subscription> Subscriptions { get; set; } = [];

    /// <summary>
    /// Clears all active subscriptions by unsubscribing from each and removing them from the subscription list.
    /// </summary>
    /// <remarks>This method iterates through all current subscriptions, unsubscribes from each one, and then
    /// clears the subscription list. After calling this method, the subscription list will be empty, and no
    /// subscriptions will remain active.</remarks>
    private static void ClearSubscriptions()
    {
        foreach(var sub in Subscriptions)
        {
            Unsubscribe(sub);
        }
        Subscriptions.Clear();
    }

    /// <summary>
    /// Subscribes to the specified subscription and ensures it is added to the collection of active subscriptions.
    /// </summary>
    /// <remarks>If the specified subscription is not already in the collection of active subscriptions, it
    /// will be added.  Disposing the returned <see cref="IDisposable"/> signals the observer that the subscription has
    /// completed.</remarks>
    /// <param name="sub">The subscription to add and monitor. Must not be null.</param>
    /// <returns>An <see cref="IDisposable"/> that, when disposed, invokes the <see cref="IObserver{T}.OnCompleted"/> method  of
    /// the subscription's observer.</returns>
    public static IDisposable Subscribe(Subscription sub)
    {
        if (!Subscriptions.Contains(sub))
        {
            Subscriptions.Add(sub);
        }
        return Disposable.Create(sub.Observer.OnCompleted);
    }

    /// <summary>
    /// Subscribes the specified observer to receive notifications of <see cref="TrackPlayerEventArgs"/> events.
    /// </summary>
    /// <remarks>The observer will receive notifications for events represented by <see
    /// cref="TrackPlayerEventArgs"/> until the subscription is disposed. Ensure the returned <see cref="IDisposable"/>
    /// is properly disposed to avoid memory leaks or unintended event handling.</remarks>
    /// <param name="sub">The observer that will receive event notifications.</param>
    /// <returns>An <see cref="IDisposable"/> object that can be used to unsubscribe the observer from receiving notifications.</returns>
    public static IDisposable Subscribe(IObserver<TrackPlayerEventArgs> sub, string name)
    {
        Subscription subscriber = new(sub, name);
        return Subscribe(subscriber);
    }

    /// <summary>
    /// Unsubscribes the specified subscriber, completing its observer and releasing associated resources.
    /// </summary>
    /// <remarks>This method ensures that the observer associated with the specified subscription is notified
    /// of completion  before the subscription is removed. The subscription is then disposed to release any resources it
    /// holds.</remarks>
    /// <param name="subscriber">The subscription to be removed. Must not be null and must exist in the current list of subscriptions.</param>
    public static void Unsubscribe(Subscription subscriber)
    {
        if (Subscriptions.Contains(subscriber))
        {
            subscriber.Observer.OnCompleted();
            Subscriptions.Remove(subscriber);
            subscriber.Dispose();
        }
    }

    public static string ToTrackString()
    {
        return $"Track Name: {AudioTrack?.Name} / Duration: {Duration} / Channels: {AudioTrack?.AudioFormat.Channels}";
    }

    /// <summary>
    /// Converts byte samples from XMP to normalized float samples (-1.0 to 1.0 range).
    /// </summary>
    private static int ConvertBytesToFloatSamples(ReadOnlySpan<byte> byteData, Span<float> floatBuffer, AudioFormatInfo formatInfo)
    {
        var bytesPerSample = formatInfo.BytesPerSample;
        var sampleCount = Math.Min(byteData.Length / bytesPerSample, floatBuffer.Length);

        if (formatInfo.BitsPerSample == 16)
        {
            // 16-bit signed PCM: -32768 to 32767 -> -1.0 to 1.0
            for (int i = 0; i < sampleCount; i++)
            {
                var sampleValue = BitConverter.ToInt16(byteData.Slice(i * 2, 2));
                floatBuffer[i] = sampleValue / 32768f;
            }
        }
        else if (formatInfo.BitsPerSample == 8)
        {
            // 8-bit unsigned PCM: 0 to 255 -> -1.0 to 1.0
            for (int i = 0; i < sampleCount; i++)
            {
                var sampleValue = byteData[i];
                floatBuffer[i] = (sampleValue - 128) / 128f;
            }
        }
        else
        {
            throw new NotSupportedException($"Unsupported bit depth: {formatInfo.BitsPerSample}");
        }

        return sampleCount;
    }

    /// <summary>
    /// Background task that continuously reads audio data from XMP and feeds it to the QueueDataProvider.
    /// </summary>
    private static async Task RenderXmpAudioAsync(AssetDataProvider dataProvider, AudioFormatInfo formatInfo, CancellationToken cancellationToken)
    {
        Logger.InfoLog(nameof(TrackPlayer), "🎵 XMP streaming task started");

        const int bufferSizeInBytes = 8192; // Read 8KB at a time
        var byteBuffer = new byte[bufferSizeInBytes];
        var floatBuffer = new float[bufferSizeInBytes / formatInfo.BytesPerSample];

        try
        {
            _currentXmpInstance!.StartPlayer();
            while (_currentXmpInstance!.ReadBuffer(byteBuffer, false) && !cancellationToken.IsCancellationRequested)
            {
                ConvertBytesToFloatSamples(byteBuffer, floatBuffer, formatInfo);
                while (dataProvider.ReadBytes(floatBuffer.AsSpan()) > 0) {
                    await Task.Yield();
                }
                // Read bytes from XMP stream

                //if (bytesRead == 0)
                //{
                //    // End of stream reached
                //    Logger.InfoLog(nameof(TrackPlayer), "🎵 XMP stream reached end");
                //    queueProvider.CompleteAdding();
                //    break;
                //}

                //// Convert bytes to float samples
                //var samplesConverted = ConvertBytesToFloatSamples(
                //    byteBuffer.AsSpan(0, bytesRead),
                //    floatBuffer.AsSpan(),
                //    formatInfo);

                //// Feed samples to queue provider
                //if (queueProvider.SamplesAvailable <= 390000)
                //{
                //    queueProvider.AddSamples(floatBuffer.AsSpan(0, samplesConverted));

                //    bytesRead = await xmpStream.ReadAsync(byteBuffer, cancellationToken);
                //}
            }
        }
        catch (OperationCanceledException)
        {
            Logger.DebugLog(nameof(TrackPlayer), "XMP streaming task cancelled");
        }
        catch (Exception ex)
        {
            Logger.ErrorLog(nameof(TrackPlayer), $"Error in XMP streaming task: {ex.Message}");
            Exceptions.Add(ex);
        }
        finally
        {

            Logger.InfoLog(nameof(TrackPlayer), "🎵 XMP streaming task completed");
        }
    }
}

/// <summary>
/// Provides data for events related to track player state changes, including status updates, playback state transitions, and exception information.
/// </summary>
/// <remarks>
/// The <see cref="TrackPlayerEventArgs"/> class encapsulates event data that is passed when the track player's state changes.
/// This includes information about the current operational status, playback state, and any exceptions that may have occurred
/// during loading or playback operations.
/// <para>
/// This class is typically used in conjunction with the <see cref="TrackPlayerEventHandler"/> delegate to provide
/// comprehensive information about track player state changes to event subscribers.
/// </para>
/// <para>
/// The exceptions collection provides access to any errors that occurred during the operation, allowing for
/// detailed error handling and diagnostics.
/// </para>
/// </remarks>
/// <param name="status">The current operational status of the track player, indicating the state of loading, processing, or playback operations.</param>
/// <param name="state">The current playback state, such as playing, paused, or stopped.</param>
/// <param name="exceptions">A collection of exceptions that occurred during the operation, if any. This collection may be empty if no errors occurred.</param>
public class TrackPlayerEventArgs(Statuses status, PlaybackState state, List<Exception> exceptions) : EventArgs
{
    /// <summary>
    /// Gets the current operational status of the track player.
    /// </summary>
    /// <value>
    /// A <see cref="Statuses"/> value indicating the current state of the track player's operations,
    /// such as loading, processing, or error conditions.
    /// </value>
    public Statuses Status = status;

    /// <summary>
    /// Gets the current playback state of the track player.
    /// </summary>
    /// <value>
    /// A <see cref="PlaybackState"/> value indicating whether the player is currently playing, paused, stopped, or in another playback state.
    /// </value>
    public PlaybackState PlaybackState = state;

    /// <summary>
    /// Gets the collection of exceptions that occurred during the track player operation.
    /// </summary>
    /// <value>
    /// A <see cref="List{Exception}"/> containing any exceptions that were encountered during loading or playback operations.
    /// This collection may be empty if no errors occurred.
    /// </value>
    public List<Exception> Exceptions = exceptions;

    /// <summary>
    /// Returns a string representation of the track player event arguments, including status, playback state, and exception count.
    /// </summary>
    /// <returns>
    /// A formatted string containing the current status, playback state, and the number of exceptions in the format:
    /// "[TrackPlayerEventArgs] Status: {Status} - Playback State: {PlaybackState} - Exceptions: {Count}"
    /// </returns>
    public override string ToString()
    {
        return $"[TrackPlayerEventArgs] Status: {Status} - Playback State: {PlaybackState} - Exceptions: {Exceptions.Count}";
    }
}

/// <summary>
/// Represents the various states that the TrackPlayer can be in.
/// </summary>
/// <remarks>This enumeration is commonly used to indicate the current status of a process or resource.  Each
/// value corresponds to a distinct state, such as loading, processing, or encountering an error.</remarks>
public enum Statuses
{
    Loading,
    Loaded,
    Ready,
    Scrubbing,
    Resuming,
    Reset,
    Error,
    PlaybackEnded
}