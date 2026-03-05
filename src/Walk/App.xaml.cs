using System.IO;
using System.Windows;
using Walk.Helpers;
using Walk.Plugins;
using Walk.Services;
using Walk.ViewModels;
using Forms = System.Windows.Forms;

namespace Walk;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;
    private HotkeyService? _hotkeyService;
    private Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _trayDefaultIcon;
    private System.Drawing.Icon? _trayActiveIcon;
    private AppIndexService? _indexService;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Walk");
        Directory.CreateDirectory(dataDir);

        // Settings
        var settingsService = new SettingsService(dataDir);
        var settings = await settingsService.LoadAsync();

        // Services
        var cacheService = new CacheService(dataDir);
        _indexService = new AppIndexService(dataDir);
        await _indexService.BuildIndexAsync();
        _indexService.StartWatching();

        // Plugins
        var plugins = new IQueryPlugin[]
        {
            new CalculatorPlugin(),
            new CurrencyPlugin(cacheService, TimeSpan.FromHours(settings.CurrencyCacheTtlHours)),
            new SystemCommandPlugin(),
            new FileSearchPlugin(),
            new AppSearchPlugin(_indexService),
        };

        var router = new QueryRouter(plugins);
        var viewModel = new MainViewModel(router, settings.MaxResults);

        // Main window
        _mainWindow = new MainWindow(viewModel);

        // Bind visibility
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.IsVisible))
            {
                if (viewModel.IsVisible)
                    _mainWindow.ShowLauncher();
                else
                    _mainWindow.HideLauncher();

                UpdateTrayIcon(viewModel.IsVisible);
            }
        };

        // Hotkey - need a window handle, show briefly then hide
        _mainWindow.Show();
        var handle = new System.Windows.Interop.WindowInteropHelper(_mainWindow).Handle;
        _mainWindow.Hide();

        _hotkeyService = new HotkeyService();
        if (!_hotkeyService.Register(handle))
        {
            System.Windows.MessageBox.Show(
                "Could not register Ctrl+Alt+Space hotkey. Another application may be using it.",
                "Walk", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _hotkeyService.HotkeyPressed += () =>
        {
            Current.Dispatcher.Invoke(() => viewModel.Toggle());
        };

        // Watch system theme
        Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();
        Wpf.Ui.Appearance.SystemThemeWatcher.Watch(_mainWindow);

        // Auto-start
        ConfigureAutoStart(settings.StartWithWindows);

        // System tray
        SetupTray(viewModel);
    }

    private void SetupTray(MainViewModel viewModel)
    {
        var contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.Add("Show Launcher", null, (_, _) =>
            Current.Dispatcher.Invoke(() => viewModel.Show()));
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add("Quit", null, (_, _) =>
            Current.Dispatcher.Invoke(() => Shutdown()));

        _trayDefaultIcon = TrayIconGenerator.Create(32);
        _trayActiveIcon = TrayIconGenerator.Create(32, active: true);

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _trayDefaultIcon,
            Text = "Walk - Ctrl+Alt+Space to launch",
            Visible = true,
            ContextMenuStrip = contextMenu,
        };

        _trayIcon.DoubleClick += (_, _) =>
            Current.Dispatcher.Invoke(() => viewModel.Show());
    }

    private static System.Drawing.Icon? LoadIconResource(string path)
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri($"pack://application:,,,/{path}", UriKind.Absolute));
        if (resource is null)
            return null;

        using var stream = resource.Stream;
        using var icon = new System.Drawing.Icon(stream);
        return (System.Drawing.Icon)icon.Clone();
    }

    private void UpdateTrayIcon(bool launcherVisible)
    {
        if (_trayIcon is null || _trayDefaultIcon is null || _trayActiveIcon is null)
            return;

        _trayIcon.Icon = launcherVisible ? _trayActiveIcon : _trayDefaultIcon;
    }

    private static void ConfigureAutoStart(bool enable)
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "Walk";

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            if (key is null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (exePath is not null)
                    key.SetValue(valueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Ignore registry errors
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _indexService?.Dispose();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _trayActiveIcon?.Dispose();
        _trayDefaultIcon?.Dispose();
        base.OnExit(e);
    }
}
