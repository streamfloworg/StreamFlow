using System.IO;
using libxmpBindings;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace StreamFlow.Core.AudioHandling;

/// <summary>
/// Provides test and diagnostic methods for XMP module playback.
/// This service encapsulates SoundFlow dependencies for module testing.
/// </summary>
public static class XmpTestService
{
    public record XmpTestResult(
        bool Success,
        string Title,
        string Message,
        string Severity
    );

    /// <summary>
    /// Tests XMP format detection and metadata retrieval for a module file.
    /// </summary>
    public static XmpTestResult TestModuleFormat(string filePath)
    {
        try
        {
            // Test the module using TestModule first
            if (!Xmp.TestModule(filePath, out var testInfo))
            {
                return new XmpTestResult(
                    false,
                    "Module Test Failed",
                    $"File: {Path.GetFileName(filePath)}\n\nThe file is not recognized as a valid module format.",
                    "Error");
            }

            // Create XMP instance and load module
            using var xmp = new Xmp(rate: 44100, format: XmpFormat.None);

            if (!xmp.LoadModule(filePath))
            {
                return new XmpTestResult(
                    false,
                    "Module Load Failed",
                    $"File: {Path.GetFileName(filePath)}\n\nFailed to load the module file.",
                    "Error");
            }

            // Get audio format information
            var formatInfo = xmp.GetAudioFormat();

            if (formatInfo == null)
            {
                return new XmpTestResult(
                    false,
                    "Format Detection Failed",
                    $"File: {Path.GetFileName(filePath)}\n\nCould not retrieve audio format information.",
                    "Warning");
            }

            // Build detailed info message
            var infoMessage = $"File: {Path.GetFileName(filePath)}\n" +
                            $"Module Name: {testInfo.Name}\n" +
                            $"Format: {testInfo.Format}\n\n" +
                            $"Audio Format:\n" +
                            $"  Sample Rate: {formatInfo.SampleRate} Hz\n" +
                            $"  Channels: {formatInfo.Channels} ({(formatInfo.IsMono ? "Mono" : "Stereo")})\n" +
                            $"  Bit Depth: {formatInfo.BitsPerSample} bit\n" +
                            $"  Format Flags: {formatInfo.Format}\n" +
                            $"  Estimated Duration: {formatInfo.EstimatedDuration:mm\\:ss}\n" +
                            $"  Block Align: {formatInfo.BlockAlign} bytes\n" +
                            $"  Avg. Bytes/Sec: {formatInfo.AverageBytesPerSecond:N0}";

            return new XmpTestResult(
                true,
                "XMP Format Test Successful ✓",
                infoMessage,
                "Success");
        }
        catch (Exception ex)
        {
            return new XmpTestResult(
                false,
                "XMP Test Error",
                $"File: {Path.GetFileName(filePath)}\n\nException: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                "Error");
        }
    }

    /// <summary>
    /// Tests XMP streaming playback with SoundFlow integration.
    /// Returns a SoundPlayer that can be controlled by the caller.
    /// </summary>
    public static async Task<(bool Success, SoundPlayer? Player, XmpTestResult Result, IDisposable? Resources)> TestModuleStreaming(string filePath)
    {
        try
        {
            // Test the module first
            if (!Xmp.TestModule(filePath, out var testInfo))
            {
                return (false, null, new XmpTestResult(
                    false,
                    "Module Test Failed",
                    $"File: {Path.GetFileName(filePath)}\n\nThe file is not recognized as a valid module format.",
                    "Error"), null);
            }

            // Create XMP instance
            var xmp = new Xmp(rate: 44100, format: XmpFormat.None);

            if (!xmp.LoadModule(filePath))
            {
                xmp.Dispose();
                return (false, null, new XmpTestResult(
                    false,
                    "Module Load Failed",
                    $"File: {Path.GetFileName(filePath)}\n\nFailed to load the module file.",
                    "Error"), null);
            }

            // Get format info
            var formatInfo = xmp.GetAudioFormat();
            if (formatInfo == null)
            {
                xmp.Dispose();
                return (false, null, new XmpTestResult(
                    false,
                    "Format Detection Failed",
                    $"File: {Path.GetFileName(filePath)}\n\nCould not retrieve audio format information.",
                    "Warning"), null);
            }

            // Open audio stream
            var stream = await xmp.OpenAudioStreamAsync(filePath, loop: false, bufferSize: 8192);
            if (stream == null)
            {
                xmp.Dispose();
                return (false, null, new XmpTestResult(
                    false,
                    "Stream Creation Failed",
                    $"File: {Path.GetFileName(filePath)}\n\nFailed to create audio stream.",
                    "Error"), null);
            }

            // Create audio format for SoundFlow
            var soundFlowFormat = new AudioFormat
            {
                SampleRate = formatInfo.SampleRate,
                Channels = formatInfo.Channels,
                Format = formatInfo.BitsPerSample == 8 ? SampleFormat.U8 : SampleFormat.S16,
                Layout = formatInfo.Channels == 1 ? ChannelLayout.Mono : ChannelLayout.Stereo
            };

            // Create RawDataProvider
            var sampleFormat = formatInfo.BitsPerSample == 8 ? SampleFormat.U8 : SampleFormat.S16;
            var dataProvider = new QueueDataProvider(soundFlowFormat);

            // Create player
            var player = new SoundPlayer(AudioEngine.Engine, soundFlowFormat, dataProvider)
            {
                Name = testInfo.Name,
                Volume = 0.7f
            };

            var playbackMessage = $"Playing: {testInfo.Name}\n" +
                                $"Format: {testInfo.Format}\n" +
                                $"Sample Rate: {formatInfo.SampleRate} Hz\n" +
                                $"Channels: {formatInfo.Channels}\n" +
                                $"Duration: {formatInfo.EstimatedDuration:mm\\:ss}\n\n" +
                                $"Playback will start now. Close this dialog to stop playback.";

            // Return player and resources for caller to manage
            var resources = new DisposableResources(xmp, stream, dataProvider, player);
            
            return (true, player, new XmpTestResult(
                true,
                "XMP Streaming Playback Test",
                playbackMessage,
                "Info"), resources);
        }
        catch (Exception ex)
        {
            return (false, null, new XmpTestResult(
                false,
                "XMP Streaming Test Error",
                $"File: {Path.GetFileName(filePath)}\n\nException: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                "Error"), null);
        }
    }

    private class DisposableResources : IDisposable
    {
        private readonly Xmp _xmp;
        private readonly Stream _stream;
        private readonly ISoundDataProvider _dataProvider;
        private readonly SoundPlayer _player;

        public DisposableResources(Xmp xmp, Stream stream, ISoundDataProvider dataProvider, SoundPlayer player)
        {
            _xmp = xmp;
            _stream = stream;
            _dataProvider = dataProvider;
            _player = player;
        }

        public void Dispose()
        {
            _player?.Stop();
            _player?.Dispose();
            _dataProvider?.Dispose();
            _stream?.Dispose();
            _xmp?.Dispose();
        }
    }
}
