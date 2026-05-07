using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace Walk.Services;

internal sealed class LowLevelKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int HC_ACTION = 0;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private readonly Func<LowLevelKeyboardEvent, bool> _handleKeyboardEvent;
    private LowLevelKeyboardProc? _hookProc;
    private IntPtr _hookHandle;

    public LowLevelKeyboardHook(Func<LowLevelKeyboardEvent, bool> handleKeyboardEvent)
    {
        _handleKeyboardEvent = handleKeyboardEvent;
    }

    public bool Install()
    {
        if (_hookHandle != IntPtr.Zero)
            return true;

        _hookProc = HookCallback;
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetCurrentModuleHandle(), 0);
        if (_hookHandle != IntPtr.Zero)
            return true;

        _hookProc = null;
        return false;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= HC_ACTION &&
            LowLevelKeyboardEvent.TryCreate(wParam.ToInt32(), lParam, out var keyboardEvent) &&
            _handleKeyboardEvent(keyboardEvent))
        {
            return new IntPtr(1);
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        _hookProc = null;
    }

    private static IntPtr GetCurrentModuleHandle()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var moduleName = process.MainModule?.ModuleName;
            if (!string.IsNullOrWhiteSpace(moduleName))
            {
                var moduleHandle = GetModuleHandle(moduleName);
                if (moduleHandle != IntPtr.Zero)
                    return moduleHandle;
            }
        }
        catch
        {
            // Fall back to the current process module below.
        }

        return GetModuleHandle(null);
    }

    internal static bool IsVirtualKeyDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    internal static void PreventStandaloneWindowsKeyActivation()
    {
        var inputs = new[]
        {
            Input.CreateKeyboard(VK_CONTROL, keyUp: false),
            Input.CreateKeyboard(VK_CONTROL, keyUp: true),
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private static bool TryGetMessageState(
        int message,
        out bool isKeyDown,
        out bool isKeyUp)
    {
        isKeyDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;
        isKeyUp = message is WM_KEYUP or WM_SYSKEYUP;
        return isKeyDown || isKeyUp;
    }

    private static ModifierKeys GetCurrentModifiers()
    {
        var modifiers = ModifierKeys.None;

        if (IsVirtualKeyDown(VK_CONTROL))
            modifiers |= ModifierKeys.Control;

        if (IsVirtualKeyDown(VK_MENU))
            modifiers |= ModifierKeys.Alt;

        if (IsVirtualKeyDown(VK_SHIFT))
            modifiers |= ModifierKeys.Shift;

        if (IsVirtualKeyDown(VK_LWIN) || IsVirtualKeyDown(VK_RWIN))
            modifiers |= ModifierKeys.Windows;

        return modifiers;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelKeyboardProc lpfn,
        IntPtr hMod,
        uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint cInputs, Input[] pInputs, int cbSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;

        public static Input CreateKeyboard(uint virtualKey, bool keyUp)
        {
            return new Input
            {
                Type = INPUT_KEYBOARD,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInputData
                    {
                        VirtualKey = (ushort)virtualKey,
                        ScanCode = 0,
                        Flags = keyUp ? KEYEVENTF_KEYUP : 0,
                        Time = 0,
                        ExtraInfo = UIntPtr.Zero,
                    },
                },
            };
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInputData Mouse;

        [FieldOffset(0)]
        public KeyboardInputData Keyboard;

        [FieldOffset(0)]
        public HardwareInputData Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInputData
    {
        public uint Message;
        public ushort LowParameter;
        public ushort HighParameter;
    }

    internal readonly record struct LowLevelKeyboardEvent(
        uint VirtualKey,
        Key Key,
        ModifierKeys ModifierKeys,
        bool IsKeyDown,
        bool IsKeyUp)
    {
        public static bool TryCreate(
            int message,
            IntPtr keyboardData,
            out LowLevelKeyboardEvent keyboardEvent)
        {
            keyboardEvent = default;

            if (!TryGetMessageState(message, out var isKeyDown, out var isKeyUp))
                return false;

            var virtualKey = (uint)Marshal.ReadInt32(keyboardData);
            keyboardEvent = new LowLevelKeyboardEvent(
                virtualKey,
                KeyInterop.KeyFromVirtualKey((int)virtualKey),
                GetCurrentModifiers(),
                isKeyDown,
                isKeyUp);
            return true;
        }
    }
}
