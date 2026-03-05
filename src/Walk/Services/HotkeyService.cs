using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Walk.Services;

public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 9000;

    private HwndSource? _source;
    private IntPtr _windowHandle;

    public event Action? HotkeyPressed;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CTRL = 0x0002;
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint VK_SPACE = 0x20;

    public bool Register(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
        _source = HwndSource.FromHwnd(windowHandle);
        _source?.AddHook(HwndHook);

        return RegisterHotKey(_windowHandle, HOTKEY_ID, MOD_CTRL | MOD_ALT | MOD_NOREPEAT, VK_SPACE);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterHotKey(_windowHandle, HOTKEY_ID);
        _source?.RemoveHook(HwndHook);
    }
}
