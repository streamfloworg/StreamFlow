using System.Runtime.InteropServices;
using System.Windows.Input;

using StreamFlow.Core.AudioProperties;

namespace StreamFlow.Core.Helpers;
public static class HotKeyManager
{
    public static event EventHandler<HotKeyEventArgs>? HotKeyPressed;

    public static int RegisterHotKey(Hotkey key)
    {
        WindowReadyEvent.WaitOne();
        _wnd?.Invoke(new RegisterHotKeyDelegate(RegisterHotKeyInternal), _hwnd, Interlocked.Increment(ref _id), (uint)key.Modifiers, (uint)key.Key);
        return Interlocked.Increment(ref _id);
    }

    public static void UnregisterHotKey(int id)
    {
        _wnd?.Invoke(new UnRegisterHotKeyDelegate(UnRegisterHotKeyInternal), _hwnd, id);
    }

    private delegate void RegisterHotKeyDelegate(IntPtr hwnd, int id, uint modifiers, uint key);
    private delegate void UnRegisterHotKeyDelegate(IntPtr hwnd, int id);

    private static void RegisterHotKeyInternal(IntPtr hwnd, int id, uint modifiers, uint key)
    {
        RegisterHotKey(hWnd: hwnd, id: id, fsModifiers: modifiers, vk: key);
    }

    private static void UnRegisterHotKeyInternal(IntPtr hwnd, int id)
    {
        UnregisterHotKey(hwnd, id);
    }

    private static void OnHotKeyPressed(HotKeyEventArgs e)
    {
        HotKeyPressed?.Invoke(null, e);
    }

    private static volatile MessageWindow? _wnd;
    private static volatile IntPtr _hwnd;
    private static readonly ManualResetEvent WindowReadyEvent = new(false);

    static HotKeyManager()
    {
        new Thread(delegate ()
        {
            Application.Run(new MessageWindow());
        })
        {
            Name = "MessageLoopThread",
            IsBackground = true
        }.Start();
    }

    private class MessageWindow : Form
    {
        public MessageWindow()
        {
            _wnd = this;
            _hwnd = Handle;
            WindowReadyEvent.Set();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                var e = new HotKeyEventArgs(hotKeyParam: m.LParam);
                OnHotKeyPressed(e);
            }

            base.WndProc(m: ref m);
        }

        protected override void SetVisibleCore(bool value)
        {
            // Ensure the window never becomes visible
            base.SetVisibleCore(false);
        }

        private const int WM_HOTKEY = 0x312;
    }

    [DllImport("user32", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private static int _id = 0; 
}

public partial class HotKeyEventArgs : EventArgs
{
    public readonly Hotkey Key;

    public HotKeyEventArgs(Hotkey key)
    {
        Key = key;
    }

    public HotKeyEventArgs(IntPtr hotKeyParam)
    {
        Key = new((Key)(((uint)hotKeyParam.ToInt64() & 0xffff0000) >> 16), (ModifierKeys)((uint)hotKeyParam.ToInt64() & 0x0000ffff));
    }
}

public partial class HotKeyEventArgs
{
    [Flags]
    public enum KeyModifiers
    {
        Alt = 1,
        Control = 2,
        Shift = 4,
        Windows = 8,
        NoRepeat = 0x4000
    }
}

[Flags]
public enum KeyModifiers
{
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
    NoRepeat = 0x4000
}