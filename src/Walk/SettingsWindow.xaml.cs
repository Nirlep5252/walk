using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Walk.Services;
using Walk.ViewModels;

namespace Walk;

public partial class SettingsWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_MAINWINDOW = 2;
    private const uint VK_SHIFT = 0x10;
    private const uint VK_CONTROL = 0x11;
    private const uint VK_MENU = 0x12;
    private const uint VK_LWIN = 0x5B;
    private const uint VK_RWIN = 0x5C;
    private const uint VK_LSHIFT = 0xA0;
    private const uint VK_RSHIFT = 0xA1;
    private const uint VK_LCONTROL = 0xA2;
    private const uint VK_RCONTROL = 0xA3;
    private const uint VK_LMENU = 0xA4;
    private const uint VK_RMENU = 0xA5;

    private readonly SettingsViewModel _viewModel;
    private LowLevelKeyboardHook? _recordingHook;
    private ModifierKeys _recordingModifiers;
    private bool _recordingHookHandled;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();

        _viewModel.SaveRequested += OnSaveRequested;
        _viewModel.CancelRequested += OnCancelRequested;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnClosed;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        ApplyWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
        ApplyWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
        ApplyWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_MAINWINDOW);
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (_viewModel.IsRecording)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key == Key.Escape)
            {
                _viewModel.CancelRecording();
                e.Handled = true;
                return;
            }

            if (HotkeyService.IsModifierKey(key))
            {
                e.Handled = true;
                return;
            }

            _viewModel.ApplyRecordedHotkey(Keyboard.Modifiers, key);
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private bool OnRecordingHookKey(LowLevelKeyboardHook.LowLevelKeyboardEvent keyboardEvent)
    {
        if (!_viewModel.IsRecording)
            return false;

        if (keyboardEvent.IsKeyUp)
        {
            UpdateRecordingModifier(keyboardEvent, isDown: false);
            return _recordingHookHandled || IsRecordingModifierKey(keyboardEvent);
        }

        if (!keyboardEvent.IsKeyDown)
            return false;

        var key = keyboardEvent.Key;
        if (key == Key.Escape)
        {
            _recordingHookHandled = true;
            Dispatcher.BeginInvoke(() => _viewModel.CancelRecording());
            return true;
        }

        if (IsRecordingModifierKey(keyboardEvent))
        {
            UpdateRecordingModifier(keyboardEvent, isDown: true);
            return true;
        }

        _recordingHookHandled = true;
        var modifiers = _recordingModifiers | keyboardEvent.ModifierKeys;
        Dispatcher.BeginInvoke(() => _viewModel.ApplyRecordedHotkey(modifiers, key));
        return true;
    }

    private void OnSaveRequested()
    {
        DialogResult = true;
        Close();
    }

    private void OnCancelRequested()
    {
        DialogResult = false;
        Close();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsViewModel.IsRecording))
            return;

        if (_viewModel.IsRecording)
        {
            RecordHotkeyButton.Focus();
            StartRecordingHook();
        }
        else
        {
            StopRecordingHook();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        StopRecordingHook();
        _viewModel.SaveRequested -= OnSaveRequested;
        _viewModel.CancelRequested -= OnCancelRequested;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Closed -= OnClosed;
    }

    private void StartRecordingHook()
    {
        StopRecordingHook();
        _recordingModifiers = ModifierKeys.None;
        _recordingHookHandled = false;
        _recordingHook = new LowLevelKeyboardHook(OnRecordingHookKey);
        _recordingHook.Install();
    }

    private void StopRecordingHook()
    {
        _recordingHook?.Dispose();
        _recordingHook = null;
        _recordingModifiers = ModifierKeys.None;
        _recordingHookHandled = false;
    }

    private static bool IsRecordingModifierKey(LowLevelKeyboardHook.LowLevelKeyboardEvent keyboardEvent)
    {
        return GetRecordingModifier(keyboardEvent) != ModifierKeys.None ||
               HotkeyService.IsModifierKey(keyboardEvent.Key);
    }

    private void UpdateRecordingModifier(LowLevelKeyboardHook.LowLevelKeyboardEvent keyboardEvent, bool isDown)
    {
        var modifier = GetRecordingModifier(keyboardEvent);
        if (modifier == ModifierKeys.None)
            return;

        if (isDown)
            _recordingModifiers |= modifier;
        else
            _recordingModifiers &= ~modifier;
    }

    private static ModifierKeys GetRecordingModifier(LowLevelKeyboardHook.LowLevelKeyboardEvent keyboardEvent)
    {
        var modifier = keyboardEvent.VirtualKey switch
        {
            VK_CONTROL or VK_LCONTROL or VK_RCONTROL => ModifierKeys.Control,
            VK_MENU or VK_LMENU or VK_RMENU => ModifierKeys.Alt,
            VK_SHIFT or VK_LSHIFT or VK_RSHIFT => ModifierKeys.Shift,
            VK_LWIN or VK_RWIN => ModifierKeys.Windows,
            _ => ModifierKeys.None,
        };

        if (modifier != ModifierKeys.None)
            return modifier;

        return keyboardEvent.Key switch
        {
            Key.LeftCtrl or Key.RightCtrl => ModifierKeys.Control,
            Key.LeftAlt or Key.RightAlt => ModifierKeys.Alt,
            Key.LeftShift or Key.RightShift => ModifierKeys.Shift,
            Key.LWin or Key.RWin => ModifierKeys.Windows,
            _ => ModifierKeys.None,
        };
    }

    private static void ApplyWindowAttribute(IntPtr handle, int attribute, int value)
    {
        if (handle == IntPtr.Zero)
            return;

        try
        {
            _ = DwmSetWindowAttribute(handle, attribute, ref value, Marshal.SizeOf<int>());
        }
        catch
        {
            // Ignore unsupported DWM attributes on older Windows builds.
        }
    }
}
