using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace StreamFlow.Core.Protocol;

/// <summary>
/// Represents a parsed StreamFlow URI for deep linking to songs and loop points
/// </summary>
/// <remarks>
/// Supports URI format: streamflow://play/{songId}?loop={loopId}&position={seconds}
/// Examples:
/// - streamflow://play/AbC123De - Play song with ID AbC123De
/// - streamflow://play/AbC123De?loop=L1aB - Play song and activate loop point with ID L1aB
/// - streamflow://play/AbC123De?position=30 - Play song starting at 30 seconds
/// - streamflow://play/AbC123De?loop=intro&position=15 - Play song, activate "intro" loop, seek to 15s
/// </remarks>
public class StreamFlowUri
{
    private const string ProtocolScheme = "streamflow";
    private const string PlayAction = "play";

    /// <summary>
    /// The audio track ID to play
    /// </summary>
    public string? AudioId { get; set; }

    /// <summary>
    /// The loop point ID or name to activate (optional)
    /// </summary>
    public string? LoopIdentifier { get; set; }

    /// <summary>
    /// The position in seconds to seek to (optional)
    /// </summary>
    public double? PositionSeconds { get; set; }

    /// <summary>
    /// Indicates whether the parsed URI is valid
    /// </summary>
    public bool IsValid { get; private set; }

    /// <summary>
    /// Error message if URI parsing failed
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Parses a streamflow:// URI string
    /// </summary>
    /// <param name="uriString">The URI string to parse</param>
    /// <returns>A StreamFlowUri instance with parsed data</returns>
    public static StreamFlowUri Parse(string uriString)
    {
        var result = new StreamFlowUri();

        try
        {
            if (string.IsNullOrWhiteSpace(uriString))
            {
                result.IsValid = false;
                result.ErrorMessage = "URI string is empty";
                return result;
            }

            // Handle both streamflow:// and streamflow: formats
            if (!uriString.StartsWith($"{ProtocolScheme}://", StringComparison.OrdinalIgnoreCase) &&
                !uriString.StartsWith($"{ProtocolScheme}:", StringComparison.OrdinalIgnoreCase))
            {
                result.IsValid = false;
                result.ErrorMessage = $"URI must start with '{ProtocolScheme}://'";
                return result;
            }

            // Normalize the URI - ensure it has ://
            var normalizedUri = uriString;
            if (!normalizedUri.Contains("://"))
            {
                normalizedUri = normalizedUri.Replace($"{ProtocolScheme}:", $"{ProtocolScheme}://");
            }

            var uri = new Uri(normalizedUri);

            if (uri.Scheme.ToLower() != ProtocolScheme)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Invalid scheme '{uri.Scheme}', expected '{ProtocolScheme}'";
                return result;
            }

            // Parse host as action
            var action = uri.Host.ToLower();
            if (action != PlayAction)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Unknown action '{action}', expected '{PlayAction}'";
                return result;
            }

            // Parse path as audio ID
            var path = uri.AbsolutePath.TrimStart('/');
            if (string.IsNullOrWhiteSpace(path))
            {
                result.IsValid = false;
                result.ErrorMessage = "Audio ID is required (e.g., streamflow://play/AUDIO_ID)";
                return result;
            }

            result.AudioId = path;

            // Parse query parameters
            if (!string.IsNullOrEmpty(uri.Query))
            {
                var queryParams = HttpUtility.ParseQueryString(uri.Query);

                // Parse loop parameter
                var loopParam = queryParams["loop"];
                if (!string.IsNullOrWhiteSpace(loopParam))
                {
                    result.LoopIdentifier = loopParam;
                }

                // Parse position parameter
                var positionParam = queryParams["position"];
                if (!string.IsNullOrWhiteSpace(positionParam))
                {
                    if (double.TryParse(positionParam, out var position))
                    {
                        if (position >= 0)
                        {
                            result.PositionSeconds = position;
                        }
                        else
                        {
                            result.IsValid = false;
                            result.ErrorMessage = "Position must be a non-negative number";
                            return result;
                        }
                    }
                    else
                    {
                        result.IsValid = false;
                        result.ErrorMessage = $"Invalid position value '{positionParam}', expected a number";
                        return result;
                    }
                }
            }

            result.IsValid = true;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.ErrorMessage = $"Failed to parse URI: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Tries to parse a streamflow:// URI string
    /// </summary>
    /// <param name="uriString">The URI string to parse</param>
    /// <param name="result">The parsed result if successful</param>
    /// <returns>True if parsing succeeded, false otherwise</returns>
    public static bool TryParse(string uriString, out StreamFlowUri result)
    {
        result = Parse(uriString);
        return result.IsValid;
    }

    /// <summary>
    /// Builds a streamflow:// URI string from the given parameters
    /// </summary>
    /// <param name="audioId">The audio track ID</param>
    /// <param name="loopIdentifier">Optional loop point ID or name</param>
    /// <param name="positionSeconds">Optional position in seconds</param>
    /// <returns>A formatted streamflow:// URI string</returns>
    public static string Build(string audioId, string? loopIdentifier = null, double? positionSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(audioId))
        {
            throw new ArgumentException("Audio ID is required", nameof(audioId));
        }

        var uriBuilder = $"{ProtocolScheme}://{PlayAction}/{audioId}";

        var queryParams = new List<string>();

        if (!string.IsNullOrWhiteSpace(loopIdentifier))
        {
            queryParams.Add($"loop={Uri.EscapeDataString(loopIdentifier)}");
        }

        if (positionSeconds.HasValue && positionSeconds.Value >= 0)
        {
            queryParams.Add($"position={positionSeconds.Value}");
        }

        if (queryParams.Any())
        {
            uriBuilder += "?" + string.Join("&", queryParams);
        }

        return uriBuilder;
    }

    /// <summary>
    /// Returns a string representation of this URI
    /// </summary>
    public override string ToString()
    {
        if (!IsValid)
        {
            return $"Invalid URI: {ErrorMessage}";
        }

        return Build(AudioId ?? "", LoopIdentifier, PositionSeconds);
    }
}
