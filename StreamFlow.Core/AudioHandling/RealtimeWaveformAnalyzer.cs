using SoundFlow.Abstracts;
using SoundFlow.Structs;

namespace StreamFlow.Core.AudioHandling;

/// <summary>
/// A real-time audio analyzer that computes 56 peak amplitude bins from the playing track.
/// </summary>
public class RealtimeWaveformAnalyzer : AudioAnalyzer
{
    public override string Name { get; set; } = "Realtime Waveform";

    private readonly float[] _bars = new float[256];
    private readonly object _lock = new();

    public RealtimeWaveformAnalyzer(AudioFormat format) : base(format)
    {
    }

    /// <summary>
    /// Safely copies and samples the latest calculated waveform bar amplitudes to the destination array.
    /// Supports dynamic upsampling/downsampling to match the destination length.
    /// </summary>
    public void GetWaveformHeights(float[] destination)
    {
        if (destination == null || destination.Length == 0)
        {
            return;
        }

        lock (_lock)
        {
            int destLen = destination.Length;
            for (int i = 0; i < destLen; i++)
            {
                // Map destination index to source index in the 256-sized array
                double sourceIndex = (double)i / destLen * 256.0;
                int idx = (int)Math.Clamp(sourceIndex, 0, 255);
                destination[i] = _bars[idx];
            }
        }
    }

    /// <summary>
    /// Processes the incoming audio buffer to update the 256 peak amplitude bins.
    /// </summary>
    protected override void Analyze(ReadOnlySpan<float> buffer, int channels)
    {
        if (buffer.Length == 0 || channels <= 0)
        {
            return;
        }

        int totalFrames = buffer.Length / channels;
        if (totalFrames == 0)
        {
            return;
        }

        lock (_lock)
        {
            int framesPerBar = totalFrames / 256;
            if (framesPerBar == 0)
            {
                // If the buffer is extremely small, map directly or duplicate values
                for (int i = 0; i < 256; i++)
                {
                    int frameIndex = i * totalFrames / 256;
                    int sampleIndex = frameIndex * channels;
                    float peak = 0f;
                    if (sampleIndex < buffer.Length)
                    {
                        peak = Math.Abs(buffer[sampleIndex]);
                    }

                    _bars[i] = Math.Min(1f, peak);
                }
            }
            else
            {
                for (int i = 0; i < 256; i++)
                {
                    float max = 0f;
                    int startFrame = i * framesPerBar;
                    int endFrame = Math.Min(startFrame + framesPerBar, totalFrames);

                    for (int frame = startFrame; frame < endFrame; frame++)
                    {
                        // Check the absolute amplitude across all channels for this frame
                        for (int ch = 0; ch < channels; ch++)
                        {
                            int sampleIndex = frame * channels + ch;
                            if (sampleIndex < buffer.Length)
                            {
                                float val = Math.Abs(buffer[sampleIndex]);
                                if (val > max)
                                {
                                    max = val;
                                }
                            }
                        }
                    }

                    _bars[i] = Math.Min(1f, max);
                }
            }
        }
    }
}
