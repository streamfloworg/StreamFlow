using System.Windows;
using StreamFlow.Core.AudioHandling;
using StreamFlow.Core.AudioProperties;
using StreamFlow.Core.Protocol;

namespace StreamFlow.App.Helpers;

/// <summary>
/// Extension methods for generating and working with StreamFlow URIs
/// </summary>
public static class UriExtensions
{
    /// <summary>
    /// Generates a streamflow:// URI for this audio track
    /// </summary>
    /// <param name="audio">The audio track</param>
    /// <param name="loopPoint">Optional loop point to include</param>
    /// <param name="positionSeconds">Optional starting position in seconds</param>
    /// <returns>A formatted streamflow:// URI string</returns>
    public static string ToStreamFlowUri(this Audio audio, LoopPoint? loopPoint = null, double? positionSeconds = null)
    {
        var loopIdentifier = loopPoint?.Id ?? loopPoint?.Name;
        return StreamFlowUri.Build(audio.Id, loopIdentifier, positionSeconds);
    }

    /// <summary>
    /// Generates a streamflow:// URI for this loop point on the given audio track
    /// </summary>
    /// <param name="loopPoint">The loop point</param>
    /// <param name="audio">The audio track containing this loop point</param>
    /// <param name="positionSeconds">Optional starting position in seconds</param>
    /// <returns>A formatted streamflow:// URI string</returns>
    public static string ToStreamFlowUri(this LoopPoint loopPoint, Audio audio, double? positionSeconds = null)
    {
        return StreamFlowUri.Build(audio.Id, loopPoint.Id, positionSeconds);
    }

    /// <summary>
    /// Copies the streamflow:// URI for this audio to the clipboard
    /// </summary>
    /// <param name="audio">The audio track</param>
    /// <param name="loopPoint">Optional loop point to include</param>
    /// <param name="positionSeconds">Optional starting position in seconds</param>
    /// <returns>True if successfully copied, false otherwise</returns>
    public static bool CopyUriToClipboard(this Audio audio, LoopPoint? loopPoint = null, double? positionSeconds = null)
    {
        try
        {
            var uri = audio.ToStreamFlowUri(loopPoint, positionSeconds);
            System.Windows.Clipboard.SetText(uri);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Copies the streamflow:// URI for this loop point to the clipboard
    /// </summary>
    /// <param name="loopPoint">The loop point</param>
    /// <param name="audio">The audio track containing this loop point</param>
    /// <param name="positionSeconds">Optional starting position in seconds</param>
    /// <returns>True if successfully copied, false otherwise</returns>
    public static bool CopyUriToClipboard(this LoopPoint loopPoint, Audio audio, double? positionSeconds = null)
    {
        try
        {
            var uri = loopPoint.ToStreamFlowUri(audio, positionSeconds);
            System.Windows.Clipboard.SetText(uri);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
