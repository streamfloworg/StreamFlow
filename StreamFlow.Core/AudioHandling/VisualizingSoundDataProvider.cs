using SoundFlow.Interfaces;
using SoundFlow.Enums;
using SoundFlow.Metadata.Models;
using System;

namespace StreamFlow.Core.AudioHandling;

/// <summary>
/// A wrapper sound data provider that intercepts ReadBytes to run real-time audio analysis
/// on pre-fader (raw) samples before volume and panning scaling are applied.
/// </summary>
public sealed class VisualizingSoundDataProvider : ISoundDataProvider
{
    private readonly ISoundDataProvider _underlying;
    private readonly RealtimeWaveformAnalyzer _analyzer;

    public VisualizingSoundDataProvider(ISoundDataProvider underlying, RealtimeWaveformAnalyzer analyzer)
    {
        _underlying = underlying ?? throw new ArgumentNullException(nameof(underlying));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));

        // Forward events
        _underlying.EndOfStreamReached += (s, e) => EndOfStreamReached?.Invoke(this, e);
        _underlying.PositionChanged += (s, e) => PositionChanged?.Invoke(this, e);
    }

    public int Position => _underlying.Position;

    public int Length => _underlying.Length;

    public bool CanSeek => _underlying.CanSeek;

    public SampleFormat SampleFormat => _underlying.SampleFormat;

    public int SampleRate => _underlying.SampleRate;

    public bool IsDisposed => _underlying.IsDisposed;

    public SoundFormatInfo? FormatInfo => _underlying.FormatInfo;

    public int ReadBytes(Span<float> buffer)
    {
        int read = _underlying.ReadBytes(buffer);
        if (read > 0)
        {
            // Process the raw read audio buffer through our WaveformAnalyzer!
            int channelCount = FormatInfo?.ChannelCount ?? 2;
            _analyzer.Process(buffer.Slice(0, read), channelCount);
        }
        return read;
    }

    public void Seek(int offset)
    {
        _underlying.Seek(offset);
    }

    public void Dispose()
    {
        _underlying.Dispose();
    }

    public event EventHandler<EventArgs>? EndOfStreamReached;
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;
}
