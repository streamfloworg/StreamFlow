using Microsoft.Win32.SafeHandles;

namespace StreamFlow.Core.Helpers.KeyboardListener;

internal class WindowsHookHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public WindowsHookHandle(nint handle) : base(true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        // Unhook
        return Native.UnhookWindowsHookEx(handle);
    }
}
