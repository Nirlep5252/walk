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

    private readonly SettingsViewModel _viewModel;

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
        if (e.PropertyName == nameof(SettingsViewModel.IsRecording) && _viewModel.IsRecording)
            RecordHotkeyButton.Focus();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.SaveRequested -= OnSaveRequested;
        _viewModel.CancelRequested -= OnCancelRequested;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Closed -= OnClosed;
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
