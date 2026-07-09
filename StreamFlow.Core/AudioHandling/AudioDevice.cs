namespace StreamFlow.Core.AudioHandling;
public class AudioDevice()
{
    //
    // Summary:
    //     The unique identifier for the device.
    public nint Id
    {
        get; set;
    }

    //
    // Summary:
    //     The name of the device.
    public string Name
    {
        get; set;
    } = string.Empty;

    //
    // Summary:
    //     Indicates whether the device is set as default.
    public bool IsDefault
    {
        get; set;
    }
}
