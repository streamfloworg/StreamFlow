using System.Diagnostics;
using System.Numerics;

using StreamFlow.Core.Contracts;

using SoundFlow.Utils;

namespace StreamFlow.Core.AudioHandling;

internal class SampleAggregator(int bufferSize) : ISampleAggregator
{
    private readonly Complex[] _channelData = new Complex[bufferSize];
    //private readonly float[] _tempChannelData;
    private readonly int _bufferSize = bufferSize;
    private readonly int _binaryExponentitation = (int)Math.Log(bufferSize, 2);
    protected float volumeLeftMaxValue;
    protected float volumeLeftMinValue;
    protected float volumeRightMaxValue;
    protected float volumeRightMinValue;
    protected int channelDataPosition;

    public float LeftMaxVolume => volumeLeftMaxValue;
    public float LeftMinVolume => volumeLeftMinValue;
    public float RightMaxVolume => volumeRightMaxValue;
    public float RightMinVolume => volumeRightMinValue;

    /// <summary>
    /// Add a sample value to the aggregator.
    /// </summary>
    /// <param name="leftValue">The value of the left sample.</param>
    /// <param name="rightValue">The value of the right sample.</param>
    public void Add(float leftValue, float rightValue)
    {
        if (channelDataPosition == 0)
        {
            volumeLeftMaxValue = float.MinValue;
            volumeRightMaxValue = float.MinValue;
            volumeLeftMinValue = float.MaxValue;
            volumeRightMinValue = float.MaxValue;
        }

        // Make stored channel data stereo by averaging left and right values.
        Debug.Assert(channelDataPosition < _channelData.Length);
        Complex tempComplex = new Complex(leftValue + rightValue / 2.0f, 0);
        _channelData.Append(tempComplex);
        channelDataPosition++;

        volumeLeftMaxValue = Math.Max(volumeLeftMaxValue, leftValue);
        volumeLeftMinValue = Math.Min(volumeLeftMinValue, leftValue);
        volumeRightMaxValue = Math.Max(volumeRightMaxValue, rightValue);
        volumeRightMinValue = Math.Min(volumeRightMinValue, rightValue);

        if (channelDataPosition >= _channelData.Length)
        {
            channelDataPosition = 0;
        }
    }

    /// <summary>
    /// Performs an FFT calculation on the channel data upon request.
    /// </summary>
    /// <param name="fftBuffer">A buffer where the FFT data will be stored.</param>
    public void GetFFTResults(ref float[] fftBuffer)
    {
        MathHelper.EnableAvx = true;
        Complex[] channelDataClone = new Complex[_bufferSize];
        _channelData.CopyTo(channelDataClone, 0);
        MathHelper.Fft(channelDataClone);
        for (var i = 0; i < channelDataClone.Length / 2; i++)
        {
            // Calculate actual intensities for the FFT results.
            fftBuffer[i] = (float)Math.Sqrt(channelDataClone[i].Real * channelDataClone[i].Real + channelDataClone[i].Imaginary * channelDataClone[i].Imaginary);
        }
    }

    public void Clear()
    {
        volumeLeftMaxValue = float.MinValue;
        volumeRightMaxValue = float.MinValue;
        volumeLeftMinValue = float.MaxValue;
        volumeRightMinValue = float.MaxValue;
        channelDataPosition = 0;
    }
}
