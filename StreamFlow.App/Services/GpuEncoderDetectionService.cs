using System.Management;

using Microsoft.Extensions.Logging;

namespace StreamFlow.App.Services;

/// <summary>
/// Picks a sensible default hardware encoder by checking which GPU vendors are present
/// (via WMI), so a fresh install streams with hardware acceleration out of the box instead
/// of defaulting to the software encoder. Purely a one-time default: once the user (or a
/// restored setting) has picked an encoder, this is never consulted again.
/// </summary>
public sealed class GpuEncoderDetectionService
{
    private readonly ILogger<GpuEncoderDetectionService> _logger;

    public GpuEncoderDetectionService(ILogger<GpuEncoderDetectionService> logger)
    {
        _logger = logger;
    }

    /// <summary>Returns the best-guess FFmpeg encoder name for the machine's GPU(s), preferring
    /// NVENC, then AMF, then QSV, falling back to the libx264 software encoder if no known
    /// hardware vendor is found (or detection itself fails).</summary>
    public string DetectBestEncoder()
    {
        try
        {
            var names = GetVideoControllerNames();

            if (names.Any(n => n.Contains("nvidia", StringComparison.OrdinalIgnoreCase)))
                return "h264_nvenc";
            if (names.Any(n => n.Contains("amd", StringComparison.OrdinalIgnoreCase) || n.Contains("radeon", StringComparison.OrdinalIgnoreCase)))
                return "h264_amf";
            if (names.Any(n => n.Contains("intel", StringComparison.OrdinalIgnoreCase)))
                return "h264_qsv";
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException)
        {
            _logger.LogWarning(ex, "GPU detection via WMI failed; defaulting to software encoder");
        }

        return "libx264";
    }

    private static List<string> GetVideoControllerNames()
    {
        using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
        return searcher.Get()
            .Cast<ManagementBaseObject>()
            .Select(o => o["Name"] as string ?? "")
            .Where(n => n.Length > 0)
            .ToList();
    }
}
