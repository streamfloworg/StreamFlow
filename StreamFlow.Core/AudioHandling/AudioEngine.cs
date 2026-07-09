using System.Diagnostics;
using System.IO;
using System.Reactive.Linq;

using CommunityToolkit.Mvvm.ComponentModel;

using MoreLinq;

using StreamFlow.Core.Data;

using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Metadata;
using SoundFlow.Metadata.Models;
using SoundFlow.Providers;
using SoundFlow.Structs;

using Logger = StreamFlow.Core.Helpers.LoggerService;
using SoundFlowPlaybackState = SoundFlow.Enums.PlaybackState;

namespace StreamFlow.Core.AudioHandling;

/// <summary>
/// Manager for all kind of audio.
/// Play, Pause and Stop audios
/// </summary>
[DebuggerDisplay("Playing Sounds: {PlayingSoundEffects.Count} - Audio Track: {CurrentAudio.Name}")]
public partial class AudioEngine : ObservableObject
{
    public static AudioEngine Instance { get; private set; } = new AudioEngine();

    // Add these new variable declarations for concurrent sound effects
    private static readonly List<SoundPlayer> ActiveSoundEffectPlayers = [];
    private static readonly Lock SoundEffectPlayersLock = new();

    // Add these to track SoundEffect state
    private static readonly Dictionary<string, DateTime> ActiveSoundEffectStartTimes = [];
    private static readonly Lock SoundEffectTrackingLock = new();

    private static readonly MiniAudioEngine _engine = new();

    public static MiniAudioEngine Engine => _engine;

    private static AudioFormat AudioFormat;

    public static AudioFormat GetAudioFormat() => AudioFormat;

    public static DeviceInfo[] GetPlaybackDevices() => Engine.PlaybackDevices;

    public static DeviceInfo[] GetCaptureDevices() => Engine.CaptureDevices;

    public static bool PlayerInitialized() => Instance.CurrentPlaybackDevice?.MasterMixer.Components.Contains(TrackPlayer.PlayerRouter) ?? false;

    [ObservableProperty]
    private static AudioPlaybackDevice? _currentPlaybackDevice;

    [ObservableProperty]
    private static AudioCaptureDevice? _currentCaptureDevice;

    public static SoundPlayer? AudioTrackPlayer { get; set; }

    public static SoundPlayer? SoundEffectPlayer { get; set; }

    public static RealtimeWaveformAnalyzer? WaveformAnalyzer { get; private set; }

    [ObservableProperty]
    private static Audio? _previousAudio;
    private static readonly IDisposable? _trackPlayerSubscription;

    private static List<SoundEffect> PlayingSoundEffects { get; set; } = [];

    static AudioEngine()
    {
        // Enumerate devices asynchronously to avoid blocking UI startup
        Task.Run(() =>
        {
            try
            {
                UpdateDevicesInfo();
                SetPlaybackDevice();
                if (Instance.CurrentPlaybackDevice == null)
                {
                    throw new SoundFlow.Exceptions.BackendException(nameof(Engine), -1, "Unable to set output device or no devices found.");
                }
                // Hook into TrackPlayer events
            }
            catch (Exception ex)
            {
                throw new SoundFlow.Exceptions.BackendException(nameof(Engine), ex.HResult, $"General exception: {ex.Message}");
            }
        });
    }

    private static readonly ReadOptions MetadataReadOptions = new()
    {
        DurationAccuracy = DurationAccuracy.AccurateScan,
        ReadAlbumArt = true,
        ReadTags = true
    };

    public static SoundFormatInfo? GetAudioMetadata(string filePath)
    {
        return SoundMetadataReader.Read(filePath, MetadataReadOptions).Value;
    }

    public static void SetPlaybackDevice(DeviceInfo? outputDevice = null, int? sampleRate = null)
    {
        try
        {
            // Use provided sample rate or default to DvdHq (48000)
            var configuredDeviceFormat = sampleRate.HasValue 
                ? new AudioFormat 
                { 
                    SampleRate = sampleRate.Value, 
                    Channels = 2, 
                    Format = SampleFormat.S16,
                    Layout = ChannelLayout.Stereo
                }
                : AudioFormat.Cd;

            AudioFormat = configuredDeviceFormat;
            var configuredDevice = outputDevice is null ? GetPlaybackDevices().Where(i => i.Name == AppModel.Instance.Settings.OutputDevice).FirstOrDefault(new DeviceInfo()) : outputDevice;
            AudioPlaybackDevice? newDevice;

            //
            // Reading the NativeDataFormat information
            //

            //var formatCount = ((DeviceInfo)configuredDevice).SupportedDataFormats.Length;

            //var formatId = ((DeviceInfo)configuredDevice).Id;
            //var formatInfo = SoundFlow.Utils.Extensions.ReadArray<NativeDataFormat>(formatId, formatCount);

            //Debug.WriteLine($"Current Device: {configuredDevice}");
            //Debug.WriteLine("Supported Formats:");
            //foreach (var format in ((DeviceInfo)configuredDevice).SupportedDataFormats)
            //{
            //    Debug.WriteLine($"Format: {format}");
            //}
            //Debug.WriteLine($"Current Device Format: {configuredDeviceFormat}");

            newDevice = Engine.InitializePlaybackDevice(configuredDevice, configuredDeviceFormat);
            if (newDevice != null)
            {
                Instance.CurrentPlaybackDevice = newDevice;
                AppModel.Instance.Settings.OutputDevice = ((DeviceInfo)Instance.CurrentPlaybackDevice!.Info!).Name;
                WaveformAnalyzer = new RealtimeWaveformAnalyzer(configuredDeviceFormat);
            }
        }
        catch (SoundFlow.Exceptions.BackendException ex)
        {
            Logger.ErrorLog(Instance.GetType(), $"SoundFlow Backend: {ex.Message}");
        }
    }

    public static void SetCaptureDevice(DeviceInfo? captureDevice = null)
    {
        try
        {
            var configuredDeviceFormat = AudioFormat.DvdHq;
            AudioFormat = configuredDeviceFormat;
            var configuredDevice = captureDevice is null ? GetCaptureDevices().Where(i => i.Name == AppModel.Instance.Settings.CaptureDevice).FirstOrDefault(new DeviceInfo()) : captureDevice;
            AudioCaptureDevice? newDevice;

            newDevice = Engine.InitializeCaptureDevice(configuredDevice, configuredDeviceFormat);
            if (newDevice != null)
            {
                Instance.CurrentCaptureDevice = newDevice;
                AppModel.Instance.Settings.CaptureDevice = ((DeviceInfo)Instance.CurrentCaptureDevice!.Info!).Name;
            }
        }
        catch (SoundFlow.Exceptions.BackendException ex)
        {
            Logger.ErrorLog(Instance.GetType(), $"SoundFlow Backend: {ex.Message}");
        }
    }

    /// <summary>
    /// Play new audio
    /// </summary>
    /// <param name="audioToPlay">The new audio to play</param>
    /// <param name="progressCallback">Optional progress callback for tracker files (0-100)</param>
    /// <param name="ct">Cancellation token</param>
    public static async Task<bool> PlayAudio(
        Audio audioToPlay, 
        Action<int>? progressCallback = null,
        CancellationToken ct = new())
    {
        var result = false;

                switch (audioToPlay)
        {
            case AudioTrack at:
                if (at != NullAudio.NullTrack && at.ValidPath)
                {
                    at.Metadata = GetAudioMetadata(at.FilePath);
                    var analyzers = new List<AudioAnalyzer>();
                    if (WaveformAnalyzer != null)
                    {
                        analyzers.Add(WaveformAnalyzer);
                    }
                    await TrackPlayer.LoadAudio(at, analyzers, progressCallback, ct);
                    EnsureDeviceRunning();
                    TrackPlayer.Play();
                    result = true;
                }
                break;

            case SoundEffect se:
                if (se is not null && se.ValidPath)
                {
                    EnsureDeviceRunning();
                    await PlaySoundEffectConcurrent(se);
                    result = true;
                }
                else
                {
                    result = false;
                }
                break;

            default:
                throw new NotSupportedException($"StreamFlow does not support playing the given type -> {FileExtension.GetExtension(audioToPlay.FilePath)}");
        }
        return await Task.FromResult(result);
    }

    private static void EnsureDeviceRunning()
    {
        if (Instance.CurrentPlaybackDevice != null && !Instance.CurrentPlaybackDevice.IsRunning)
        {
            Instance.CurrentPlaybackDevice.Start();
        }
        else if (Instance.CurrentPlaybackDevice == null)
        {
            InitializeMixer();
        }
    }

    private static async Task PlaySoundEffectConcurrent(SoundEffect soundEffect)
    {
        await Task.Run(() =>
        {
            FileStream? audioStream = null;
            AssetDataProvider? assetData = null;
            SoundPlayer? soundPlayer = null;

            try
            {
                audioStream = new FileStream(soundEffect.FilePath, FileMode.Open, FileAccess.Read);
                assetData = new AssetDataProvider(Engine, AudioFormat, audioStream);
                soundPlayer = new SoundPlayer(Engine, AudioFormat, assetData)
                {
                    Volume = (float)soundEffect.Volume
                };

                if (Instance.CurrentPlaybackDevice != null)
                {
                    lock (SoundEffectPlayersLock)
                    {
                        ActiveSoundEffectPlayers.Add(soundPlayer);
                    }

                    // Track playing sound effect
                    lock (SoundEffectTrackingLock)
                    {
                        ActiveSoundEffectStartTimes[soundEffect.Name] = DateTime.UtcNow;
                    }

                    Instance.CurrentPlaybackDevice.MasterMixer.AddComponent(soundPlayer);

                    // Start playing immediately
                    soundPlayer.Play();

                    // Set up cleanup when sound effect finishes
                    Task.Run(async () =>
                    {
                        // Wait for the sound effect to finish
                        while (soundPlayer.State == SoundFlowPlaybackState.Playing)
                        {
                            await Task.Delay(50);
                        }

                        // Clean up
                        try
                        {
                            Instance.CurrentPlaybackDevice.MasterMixer.RemoveComponent(soundPlayer);
                            lock (SoundEffectPlayersLock)
                            {
                                ActiveSoundEffectPlayers.Remove(soundPlayer);
                            }
                            lock (SoundEffectTrackingLock)
                            {
                                ActiveSoundEffectStartTimes.Remove(soundEffect.Name);
                            }

                            // CRITICAL FIX: Properly dispose all resources
                            try
                            {
                                soundPlayer?.Dispose();
                                assetData?.Dispose();
                                audioStream?.Dispose();
                            }
                            catch (Exception disposeEx)
                            {
                                Logger.ErrorLog(Instance.GetType(), $"Sound Effect Disposal Error: {disposeEx.Message}");
                            }
                        }
                        catch (Exception cleanupEx)
                        {
                            Logger.ErrorLog(Instance.GetType(), $"Sound Effect Cleanup Error: {cleanupEx.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.ErrorLog(Instance.GetType(), $"Sound Effect Error Playing : {ex.Message}");

                // CRITICAL FIX: Clean up resources if we fail during setup
                try
                {
                    soundPlayer?.Dispose();
                    assetData?.Dispose();
                    audioStream?.Dispose();
                }
                catch (Exception disposeEx)
                {
                    Logger.ErrorLog(Instance.GetType(), $"Sound Effect Error Disposal: {disposeEx.Message}");
                }
            }
        });
    }

    public static void UpdateDevicesInfo()
    {
        Engine.UpdateAudioDevicesInfo();
    }

    public static async Task<bool> PlayScene(Scene sceneToPlay)
    {
        var result = false;
        // Stop all sound effects
        StopSoundEffect();

        // If no audio track should be playing stop the current audio track else transition to the audio track.
        if (sceneToPlay.SceneAudioTrack != null)
        {
            result = await PlayAudio(sceneToPlay.SceneAudioTrack);
        }
        else
        {
            StopAudioTrack();
        }

        foreach (var sf in sceneToPlay.SceneSoundEffects)
        {
            result = await PlayAudio(sf);
        }
        return await Task.FromResult(result);
    }

    private static void StopSoundEffect()
    {
        PlayingSoundEffects.Clear();
        SoundEffectPlayer?.Stop();
    }

    /// <summary>
    /// Force stop all currently playing audios
    /// </summary>
    public static void StopAllAudio()
    {
        StopAudioTrack();
        StopAllSoundEffects();
    }

    private static void StopAllSoundEffects()
    {
        lock (SoundEffectPlayersLock)
        {
            foreach (var player in ActiveSoundEffectPlayers.ToList())
            {
                try
                {
                    player.Stop();
                    Instance.CurrentPlaybackDevice?.MasterMixer.RemoveComponent(player);
                    player.Dispose();
                }
                catch { /* Ignore cleanup errors */ }
            }
            ActiveSoundEffectPlayers.Clear();
        }
        
        // Clear old sound effect queue as well
        PlayingSoundEffects.Clear();
        SoundEffectPlayer?.Stop();
    }

    /// <summary>
    /// Force stop all currently playing audios
    /// </summary>
    public static void StopAudioTrack()
    {
        TrackPlayer.Stop();
        if (Instance.CurrentPlaybackDevice?.IsRunning == true)
        {
            Instance.CurrentPlaybackDevice?.Stop();
        }
        if (TrackPlayer.PlayerRouter != null)
        {
            if (Instance.CurrentPlaybackDevice?.MasterMixer.Components.Where(x => x.Name == TrackPlayer.PlayerRouter?.Name).FirstOrDefault() is SoundPlayer player && player != null)
            {
                Instance.CurrentPlaybackDevice?.MasterMixer.RemoveComponent(player);
            }
        }
    }

    /// <summary>
    /// Attempt to seek to the provided playback position (in seconds).
    /// Returns true if the underlying player supports seeking and the operation was requested.
    /// </summary>
    public static bool TrySeek(double milleseconds)
    {
        if (AudioTrackPlayer == null)
        {
            return false;
        }

        if (milleseconds < 0)
        {
            milleseconds = 0;
        }

        return TrackPlayer.Seek(TimeSpan.FromMilliseconds(milleseconds));
    }

    #region Controls

    private static void InitializeMixer(string OutputDevice = "")
    {
        if (Instance.CurrentPlaybackDevice != null)
        {
            if (Instance.CurrentPlaybackDevice.IsRunning)
            {
                Instance.CurrentPlaybackDevice.Stop();
            }
        }
        else
        {
            Instance.CurrentPlaybackDevice = Engine.InitializePlaybackDevice(GetPlaybackDevices()?.Where(d => d.Name == OutputDevice).FirstOrDefault(), AudioFormat);
        }
    }

    /// <summary>
    /// Pause or Resume the current AudioTrack (Only for <seealso cref="AudioTrack"/>)
    /// </summary>
    /// <returns>if true, audio is paused or resumed, if false, audio couldn't be paused or resumed</returns>
    public static async Task PlayPauseAudio(PlaybackState? playbackState = null)
    {
        switch (playbackState)
        {
            case PlaybackState.Playing:
                TrackPlayer.Pause();
                break;
            case PlaybackState.Paused:
                TrackPlayer.Play();
                break;

        }
        await Task.CompletedTask;
    }
    #endregion
    
    // Add method to check if a sound effect is playing
    public static bool IsSoundEffectPlaying(string soundEffectName)
    {
        lock (SoundEffectTrackingLock)
        {
            return ActiveSoundEffectStartTimes.ContainsKey(soundEffectName);
        }
    }

    // Add method to get currently playing sound effects
    public static string[] GetPlayingSoundEffects()
    {
        lock (SoundEffectTrackingLock)
        {
            return [.. ActiveSoundEffectStartTimes.Keys];
        }
    }

    public static void RemovePlayer()
    {
        if (TrackPlayer.PlayerRouter != null)
        {
            Instance.CurrentPlaybackDevice?.MasterMixer.RemoveComponent(TrackPlayer.PlayerRouter);
        }
        else
        {
            throw new NullReferenceException("TrackPlayer.PlayerRouter is null");
        }
    }
}

[Flags]
public enum Channels : uint
{
    FrontLeft = 0x1,
    FrontRight = 0x2,
    FrontCenter = 0x4,
    Lfe = 0x8,
    BackLeft = 0x10,
    BackRight = 0x20,
    FrontLeftOfCenter = 0x40,
    FrontRightOfCenter = 0x80,
    BackCenter = 0x100,
    SideLeft = 0x200,
    SideRight = 0x400,
    TopCenter = 0x800,
    TopFrontLeft = 0x1000,
    TopFrontCenter = 0x2000,
    TopFrontRight = 0x4000,
    TopBackLeft = 0x8000,
    TopBackCenter = 0x10000,
    TopBackRight = 0x20000
}

public class AudioFormatExtended
{
    public WaveformatEncoding Encoding;
    //
    // Summary:
    //     Gets the inverse of the sample rate.
    public float InverseSampleRate => 1f / SampleRate;

    //
    // Summary:
    //     Gets or sets the sample format (e.g., S16, F32).
    public SampleFormat Format;

    //
    // Summary:
    //     Gets or sets the number of audio channels (e.g., 1 for mono, 2 for stereo).
    public int Channels;

    //
    // Summary:
    //     Gets or sets the sample rate in Hertz (e.g., 44100, 48000).
    public int SampleRate;

    public int BitsPerSample;
}

public enum SampleFormatExtended
{
    /*
     * START - Original formats from SoundFlow's SampleFormat enum
     */

    //
    // Summary:
    //     Unknown sample format.
    Unknown,
    //
    // Summary:
    //     Unsigned 8-bit format.
    U8,
    //
    // Summary:
    //     Signed 16-bit format.
    S16,
    //
    // Summary:
    //     Signed 24-bit format.
    S24,
    //
    // Summary:
    //     Signed 32-bit format.
    S32,
    //
    // Summary:
    //     32-bit floating point format.
    F32

    /*
    * END - Original formats from SoundFlow's SampleFormat enum
    */



//S = signed integer number, U = unsigned integer number.
//s8 (signed integer 8-bit number) can store numbers in range -128...127. Numbers that represent audio data of a WAV file, will be stored in this range. E.g., 90 presented as 01011010, or 0x5A; -50 presented as 11001110 (the very first 0/1 digit means +/- sign).
//s16 (signed integer 16-bit number) can store numbers in range -32 768...32 767. E.g., 5678 presented as 00010100 00101110.
//s24 (signed integer 24-bit number) can store numbers in range -8 388 608...8 388 607. E.g., 1 874 651 presented as 00011100 10011010 11011011.
//s32 (signed integer 32-bit number) can store numbers in range -2 147 483 648...2 147 483 647. E.g., 658 943 135 presented as 00100111 01000110 10101100 10011111 (32 binary digits, or 32 bits).
//u16 (unsigned integer 16-bit number) can store numbers in range 0...65 535.
//u24 (unsigned integer 24-bit number) can store numbers in range 0...16 777 215. (For example, a 24-bit-color display supports 16 777 216 different colors).
//u32 (unsigned integer 32-bit number) can store numbers in range 0...4 294 967 295.
//
//
//
//alaw     PCM A-law
//f32be    PCM 32-bit floating-point big-endian
//f32le    PCM 32-bit floating-point little-endian
//f64be    PCM 64-bit floating-point big-endian
//f64le    PCM 64-bit floating-point little-endian
//mulaw    PCM mu-law
//s16be    PCM signed 16-bit big-endian
//s16le    PCM signed 16-bit little-endian
//s24be    PCM signed 24-bit big-endian
//s24le    PCM signed 24-bit little-endian
//s32be    PCM signed 32-bit big-endian
//s32le    PCM signed 32-bit little-endian
//s8       PCM signed 8-bit
//u16be    PCM unsigned 16-bit big-endian
//u16le    PCM unsigned 16-bit little-endian
//u24be    PCM unsigned 24-bit big-endian
//u24le    PCM unsigned 24-bit little-endian
//u32be    PCM unsigned 32-bit big-endian
//u32le    PCM unsigned 32-bit little-endian
//u8       PCM unsigned 8-bit
//

}