using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace Walk.Services;

public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 9000;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CTRL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    private HwndSource? _source;
    private IntPtr _windowHandle;
    private bool _isRegistered;

    public event Action? HotkeyPressed;

    public const string DefaultModifiers = "Ctrl+Alt";
    public const string DefaultKey = "Space";

    private static readonly string[] ModifierDisplayOrder = ["Ctrl", "Alt", "Shift", "Win"];
    private static readonly IReadOnlyDictionary<string, uint> ModifierMap =
        new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ctrl"] = MOD_CTRL,
            ["Alt"] = MOD_ALT,
            ["Shift"] = MOD_SHIFT,
            ["Win"] = MOD_WIN,
        };

    private static readonly IReadOnlyDictionary<string, Key> KeyMap = BuildKeyMap();

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public bool Register(IntPtr windowHandle, string modifiers, string key, out string errorMessage)
    {
        EnsureWindowHook(windowHandle);
        UnregisterCurrentHotkey();

        if (!TryParseHotkey(modifiers, key, out var parsedModifiers, out var parsedKey, out var displayText))
        {
            errorMessage = "The selected hotkey is not supported.";
            return false;
        }

        if (!RegisterHotKey(_windowHandle, HOTKEY_ID, parsedModifiers | MOD_NOREPEAT, parsedKey))
        {
            errorMessage = $"Could not register {displayText}. Another application may be using it.";
            return false;
        }

        _isRegistered = true;
        errorMessage = string.Empty;
        return true;
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

    public static bool TryParseHotkey(
        string modifiers,
        string key,
        out uint modifierFlags,
        out uint virtualKey,
        out string displayText)
    {
        modifierFlags = 0;
        virtualKey = 0;

        if (!TryNormalizeModifiers(modifiers, out modifierFlags, out var normalizedModifiers) ||
            !TryNormalizeKey(key, out virtualKey, out var normalizedKey))
        {
            displayText = string.Empty;
            return false;
        }

        displayText = $"{normalizedModifiers}+{normalizedKey}";
        return true;
    }

    public static bool TryCreateHotkey(
        ModifierKeys modifiers,
        Key key,
        out string normalizedModifiers,
        out string normalizedKey,
        out string displayText,
        out string errorMessage)
    {
        normalizedModifiers = string.Empty;
        normalizedKey = string.Empty;
        displayText = string.Empty;

        if (IsModifierKey(key))
        {
            errorMessage = "Press at least one modifier and one non-modifier key.";
            return false;
        }

        if (!TryNormalizeModifiers(modifiers, out _, out normalizedModifiers))
        {
            errorMessage = "Include at least one modifier key in the shortcut.";
            return false;
        }

        if (!TryNormalizeKey(key, out _, out normalizedKey))
        {
            errorMessage = "That key is not supported for launcher hotkeys.";
            return false;
        }

        displayText = $"{normalizedModifiers}+{normalizedKey}";
        errorMessage = string.Empty;
        return true;
    }

    public static string FormatDisplayText(string modifiers, string key)
    {
        return TryParseHotkey(modifiers, key, out _, out _, out var displayText)
            ? displayText
            : $"{CoerceModifiers(modifiers)}+{CoerceKey(key)}";
    }

    public static string CoerceModifiers(string modifiers)
    {
        return TryNormalizeModifiers(modifiers, out _, out var normalizedModifiers)
            ? normalizedModifiers
            : DefaultModifiers;
    }

    public static string CoerceKey(string key)
    {
        return TryNormalizeKey(key, out _, out var normalizedKey)
            ? normalizedKey
            : DefaultKey;
    }

    private void EnsureWindowHook(IntPtr windowHandle)
    {
        if (_windowHandle == windowHandle && _source is not null)
            return;

        _source?.RemoveHook(HwndHook);
        _windowHandle = windowHandle;
        _source = HwndSource.FromHwnd(windowHandle);
        _source?.AddHook(HwndHook);
    }

    private void UnregisterCurrentHotkey()
    {
        if (_isRegistered && _windowHandle != IntPtr.Zero)
        {
            UnregisterHotKey(_windowHandle, HOTKEY_ID);
            _isRegistered = false;
        }
    }

    private static bool TryNormalizeModifiers(string modifiers, out uint modifierFlags, out string normalizedModifiers)
    {
        modifierFlags = 0;
        normalizedModifiers = string.Empty;

        if (string.IsNullOrWhiteSpace(modifiers))
            return false;

        var selectedModifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in modifiers.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!ModifierMap.TryGetValue(token, out var modifierFlag) || !selectedModifiers.Add(token))
                return false;

            modifierFlags |= modifierFlag;
        }

        if (modifierFlags == 0)
            return false;

        normalizedModifiers = string.Join(
            "+",
            ModifierDisplayOrder.Where(selectedModifiers.Contains));

        return normalizedModifiers.Length > 0;
    }

    private static bool TryNormalizeModifiers(ModifierKeys modifiers, out uint modifierFlags, out string normalizedModifiers)
    {
        modifierFlags = 0;
        normalizedModifiers = string.Empty;

        var selectedModifiers = new List<string>();

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            modifierFlags |= MOD_CTRL;
            selectedModifiers.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            modifierFlags |= MOD_ALT;
            selectedModifiers.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            modifierFlags |= MOD_SHIFT;
            selectedModifiers.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            modifierFlags |= MOD_WIN;
            selectedModifiers.Add("Win");
        }

        if (modifierFlags == 0)
            return false;

        normalizedModifiers = string.Join("+", selectedModifiers);
        return true;
    }

    private static bool TryNormalizeKey(string key, out uint virtualKey, out string normalizedKey)
    {
        virtualKey = 0;
        normalizedKey = string.Empty;

        if (string.IsNullOrWhiteSpace(key))
            return false;

        var trimmedKey = key.Trim();
        if (!KeyMap.TryGetValue(trimmedKey, out var parsedKey))
            return false;

        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(parsedKey);
        normalizedKey = KeyMap.Keys.First(option => option.Equals(trimmedKey, StringComparison.OrdinalIgnoreCase));
        return virtualKey != 0;
    }

    private static bool TryNormalizeKey(Key key, out uint virtualKey, out string normalizedKey)
    {
        virtualKey = 0;
        normalizedKey = string.Empty;

        var effectiveKey = key switch
        {
            Key.System => Key.None,
            Key.LeftCtrl or Key.RightCtrl => Key.None,
            Key.LeftAlt or Key.RightAlt => Key.None,
            Key.LeftShift or Key.RightShift => Key.None,
            Key.LWin or Key.RWin => Key.None,
            _ => key,
        };

        if (effectiveKey == Key.None)
            return false;

        var lookup = effectiveKey switch
        {
            >= Key.A and <= Key.Z => effectiveKey.ToString(),
            >= Key.D0 and <= Key.D9 => ((int)(effectiveKey - Key.D0)).ToString(),
            >= Key.F1 and <= Key.F24 => effectiveKey.ToString(),
            Key.Space => "Space",
            _ => string.Empty,
        };

        if (lookup.Length == 0 || !KeyMap.TryGetValue(lookup, out var mappedKey))
            return false;

        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(mappedKey);
        normalizedKey = lookup;
        return virtualKey != 0;
    }

    public static bool IsModifierKey(Key key)
    {
        return key is
            Key.LeftCtrl or Key.RightCtrl or
            Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or
            Key.LWin or Key.RWin;
    }

    private static IReadOnlyDictionary<string, Key> BuildKeyMap()
    {
        var keys = new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase)
        {
            ["Space"] = Key.Space,
        };

        foreach (var letter in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
            keys[letter.ToString()] = Enum.Parse<Key>(letter.ToString());

        for (var digit = 0; digit <= 9; digit++)
            keys[digit.ToString()] = Enum.Parse<Key>($"D{digit}");

        for (var index = 1; index <= 12; index++)
            keys[$"F{index}"] = Enum.Parse<Key>($"F{index}");

        return keys;
    }

    public void Dispose()
    {
        UnregisterCurrentHotkey();
        _source?.RemoveHook(HwndHook);
    }
}
