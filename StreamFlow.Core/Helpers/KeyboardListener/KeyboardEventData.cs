using System.Runtime.InteropServices;

namespace StreamFlow.Core.Helpers.KeyboardListener;

[StructLayout(LayoutKind.Sequential)]
public struct KeyboardEventData
{
    public int VirtualKeyCode;
    public int HardwareScanCode;
    public int Flags;
    public int Time;
    public nint ExtraInfo;
}
