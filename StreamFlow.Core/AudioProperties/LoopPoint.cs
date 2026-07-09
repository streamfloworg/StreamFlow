using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StreamFlow.Core.AudioProperties;

/// <summary>
/// Represents a loop point with a defined start and end time, allowing for optional enforcement of the specified order.
/// </summary>
/// <remarks>The <see cref="LoopPoint"/> class is used to define a looping range with a start and end time,
/// represented as <see cref="TimeSpan"/> values. It provides constructors for initializing loop points with various
/// input types (e.g., <see cref="float"/>, <see cref="int"/>, <see cref="double"/>), and includes settings to enforce or
/// adjust the order of the start and end times.</remarks>
public class LoopPoint
{
    public LoopPoint()
    {
        // Ensure ID is generated on creation
        _ = Id;
    }

    /// <summary>
    /// Unique short identifier for this loop point, used for URI protocol deep linking
    /// </summary>
    /// <remarks>Automatically generated on first access if not set. 4-character URL-safe string.</remarks>
    private string? _id;
    public string Id
    {
        get
        {
            if (string.IsNullOrEmpty(_id))
            {
                _id = GenerateShortId();
            }
            return _id;
        }
        set
        {
            _id = value;
        }
    }

    /// <summary>
    /// Generates a short, URL-safe unique identifier (4 characters)
    /// </summary>
    private static string GenerateShortId()
    {
        // Generate a short ID using base64-encoded GUID (first 4 chars)
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "")
            .Substring(0, 4);
    }

    /// <summary>
    /// Represents the name of the loop point.
    /// </summary>
    /// <remarks>This property provides a user-friendly identifier for the loop point. If no name is provided,
    /// a default name may be generated based on the loop point's position.</remarks>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Represents the duration of time at which the loop sample starts.
    /// </summary>
    /// <remarks>This property specifies the starting point of a loop sample as a <see cref="TimeSpan"/>.  Ensure
    /// that the value is non-negative to avoid unexpected behavior.</remarks>
    public TimeSpan StartLoopSample { get; set; }

    /// <summary>
    /// Represents the duration of the end loop sample.
    /// </summary>
    /// <remarks>This property is used to specify the time span associated with the end of a loop sample.  The
    /// value should be a valid <see cref="TimeSpan"/> representing the desired duration.</remarks>
    public TimeSpan EndLoopSample { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LoopPoint"/> class, representing a loop point with specified start
    /// and end times, optionally enforcing the provided order.
    /// </summary>
    /// <remarks>If <paramref name="force"/> is <see langword="false"/>, the constructor ensures that the
    /// <paramref name="startLoopSample"/> is less than or equal to <paramref name="endLoopSample"/> by swapping the
    /// values if necessary.</remarks>
    /// <param name="startLoopSample">The starting point of the loop, specified as a <see cref="TimeSpan"/>.</param>
    /// <param name="endLoopSample">The ending point of the loop, specified as a <see cref="TimeSpan"/>.</param>
    /// <param name="name">The optional name for the loop point. If not provided, an empty string is used.</param>
    /// <param name="force">A boolean value indicating whether to enforce the provided <paramref name="startLoopSample"/> and <paramref
    /// name="endLoopSample"/> values as-is. If <see langword="true"/>, the values are used directly. If <see
    /// langword="false"/>, the values are adjusted to ensure the start time is less than or equal to the end time.</param>
    public LoopPoint(TimeSpan startLoopSample, TimeSpan endLoopSample, string name = "", bool force = false)
    {
        Name = name;
        if (force)
        {
            StartLoopSample = startLoopSample;
            EndLoopSample = endLoopSample;
        }
        else
        {
            var comparison = TimeSpan.Compare(startLoopSample, endLoopSample);
            StartLoopSample = comparison <= 0 ? startLoopSample : endLoopSample;
            EndLoopSample = comparison >= 0 ? startLoopSample : endLoopSample;
        }
    }

    /// <summary>
    /// Gets the minimum time span recorded during the start loop sample.
    /// </summary>
    /// <returns>A <see cref="TimeSpan"/> representing the minimum recorded duration.</returns>
    public TimeSpan Min() => StartLoopSample;

    /// <summary>
    /// Gets the maximum duration represented by the end of the loop sample.
    /// </summary>
    /// <returns>A <see cref="TimeSpan"/> representing the maximum duration.</returns>
    public TimeSpan Max() => EndLoopSample;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoopPoint"/> class with the specified loop start and end sample
    /// positions.  
    /// </summary>
    /// <param name="startLoopSample">The sample position where the loop starts. Must be a non-negative value.</param>
    /// <param name="endLoopSample">The sample position where the loop ends. Must be greater than or equal to <paramref name="startLoopSample"/>.</param>
    /// <param name="name">The optional name for the loop point. If not provided, an empty string is used.</param>
    /// <param name="force">A value indicating whether to force the loop point, even if the specified range is invalid. Defaults to <see
    /// langword="false"/>.</param>
    public LoopPoint(float startLoopSample, float endLoopSample, string name = "", bool force = false) : this((double)startLoopSample, endLoopSample, name, force) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="LoopPoint"/> class with the specified start and end loop sample
    /// positions.  
    /// </summary>
    /// <remarks>This constructor allows specifying loop points using integer sample indices. If <paramref
    /// name="force"/> is set to <see langword="true"/>, the constructor bypasses validation checks, which may result in
    /// undefined behavior if the parameters are invalid.</remarks>
    /// <param name="startLoopSample">The sample index at which the loop starts. Must be a non-negative integer.</param>
    /// <param name="endLoopSample">The sample index at which the loop ends. Must be greater than or equal to <paramref name="startLoopSample"/>.</param>
    /// <param name="name">The optional name for the loop point. If not provided, an empty string is used.</param>
    /// <param name="force">A value indicating whether to force the loop point creation even if the specified parameters are invalid. If
    /// <see langword="true"/>, the loop point will be created regardless of parameter validation; otherwise, validation
    /// rules are enforced.</param>
    public LoopPoint(int startLoopSample, int endLoopSample, string name = "", bool force = false) : this((double)startLoopSample, endLoopSample, name, force) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="LoopPoint"/> class with the specified start and end loop points,
    /// optionally forcing the loop behavior.
    /// </summary>
    /// <param name="startLoopSample">The starting point of the loop, specified as a sample position in seconds.</param>
    /// <param name="endLoopSample">The ending point of the loop, specified as a sample position in seconds.</param>
    /// <param name="name">The optional name for the loop point. If not provided, an empty string is used.</param>
    /// <param name="force">A value indicating whether to force the loop behavior. <see langword="true"/> to enforce the loop; otherwise,
    /// <see langword="false"/>.</param>
    public LoopPoint(double startLoopSample, double endLoopSample, string name = "", bool force = false) : this(TimeSpan.FromSeconds(startLoopSample), TimeSpan.FromSeconds(endLoopSample), name, force) { }
}
 