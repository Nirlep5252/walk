using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Walk.Helpers;
using Walk.Services;

namespace Walk;

public partial class WhatsNewWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_MAINWINDOW = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    public WhatsNewWindow()
    {
        InitializeComponent();
        ReleaseNotesTextBox.Text = "No changelog available yet.";
    }

    public WhatsNewWindow(ChangelogEntry entry, bool mandatory)
    {
        InitializeComponent();
        ReleaseNotesTextBox.Text = ReleaseNotesFormatter.ToDisplayText(entry.Markdown);
        Title = $"What's New - Walk {AppVersionService.FormatVersionBadge(entry.Version)}";
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        ApplyWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
        ApplyWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
        ApplyWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_MAINWINDOW);
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
