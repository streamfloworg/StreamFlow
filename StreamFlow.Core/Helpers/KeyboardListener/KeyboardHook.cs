using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StreamFlow.Core.Helpers.KeyboardListener;

public class KeyboardHook : IDisposable
{
    public delegate void OnKeyboardEventHandler(object sender, KeyboardEventArgs e);

    public event OnKeyboardEventHandler OnKeyboard;

    private WindowsHookHandle _hookHandle;
    private Native.LowLevelKeyboardProc _keyboardProc;

    public KeyboardHook()
    {
        // Assign keyboard message handler
        _keyboardProc = KeyboardCallback;
        using (var currentProcess = Process.GetCurrentProcess())
        {
            using (var module = currentProcess.MainModule)
            {
                // Set windows hook
                _hookHandle = new WindowsHookHandle(Native.SetWindowsHookEx((int)WindowsHookType.WH_KEYBOARD_LL, _keyboardProc,
                    Native.GetModuleHandle(module.ModuleName), 0));
            }
        }
    }

    private nint KeyboardCallback(int nCode, nint wParam, nint lParam)
    {
        // Message should be ignored if nCode is negative
        if (nCode >= 0)
        {
            // Get key state
            var state = (KeyState)wParam.ToInt32();
            // Get key data
            var data = Marshal.PtrToStructure<KeyboardEventData>(lParam);
            // Create event arguments
            var args = new KeyboardEventArgs(state, data);
            // Invoke event
            OnKeyboard?.Invoke(this, args);
            // Return nonzero value to prevent passing on
            if (args.Handled)
            {
                return 1;
            }
        }

        return Native.CallNextHookEx(_hookHandle.DangerousGetHandle(), nCode, wParam, lParam);
    }

    public void Dispose()
    {
        _hookHandle?.Dispose();
    }
}
