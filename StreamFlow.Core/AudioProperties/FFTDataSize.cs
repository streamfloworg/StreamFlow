namespace StreamFlow.Core.AudioProperties;

/// <summary>
/// Specifies the available data sizes for Fast Fourier Transform (FFT) operations.
/// </summary>
/// <remarks>Use this enumeration to select the number of points for FFT processing. The value corresponds to the
/// total number of points in the FFT, which determines the frequency resolution and computational cost. Larger sizes
/// provide higher frequency resolution but require more processing time and memory.</remarks>
public enum FFTDataSize : int
{
    /// <summary>
    /// A 256 point FFT. Real data will be 128 floating point values.
    /// </summary>
    FFT256 = 256,
    /// <summary>
    /// A 512 point FFT. Real data will be 256 floating point values.
    /// </summary>
    FFT512 = 512,
    /// <summary>
    /// A 1024 point FFT. Real data will be 512 floating point values.
    /// </summary>
    FFT1024 = 1024,
    /// <summary>
    /// A 2048 point FFT. Real data will be 1024 floating point values.
    /// </summary>
    FFT2048 = 2048,
    /// <summary>
    /// A 4096 point FFT. Real data will be 2048 floating point values.
    /// </summary>
    FFT4096 = 4096,
    /// <summary>
    /// A 8192 point FFT. Real data will be 4096 floating point values.
    /// </summary>
    FFT8192 = 8192,
    /// <summary>
    /// A 16384 point FFT. Real data will be 8192 floating point values.
    /// </summary>
    FFT16384 = 16384
}
