using System.IO;
using System.Windows;
using System.Windows.Interop;
using Walk.Helpers;
using Walk.Plugins;
using Walk.Services;
using Walk.ViewModels;
using Forms = System.Windows.Forms;

namespace Walk;

public partial class App : System.Windows.Application
{
    private SingleInstanceManager? _singleInstanceManager;
    private MainWindow? _mainWindow;
    private HotkeyService? _hotkeyService;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ToolStripMenuItem? _trayStatusItem;
    private Forms.ToolStripMenuItem? _checkForUpdatesItem;
    private System.Drawing.Icon? _trayDefaultIcon;
    private System.Drawing.Icon? _trayActiveIcon;
    private AppIndexService? _indexService;
    private UpdateService? _updateService;
    private SettingsService? _settingsService;
    private RunHistoryService? _runHistoryService;
    private WalkSettings _settings = new();
    private SettingsWindow? _settingsWindow;
    private SettingsViewModel? _settingsViewModel;

    public App()
    {
    }

    public App(SingleInstanceManager singleInstanceManager)
    {
        _singleInstanceManager = singleInstanceManager;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstanceManager ??= new SingleInstanceManager("Walk");

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Walk");
        Directory.CreateDirectory(dataDir);

        // Settings
        _settingsService = new SettingsService(dataDir);
        _settings = await _settingsService.LoadAsync();

        // Services
        var cacheService = new CacheService(dataDir);
        _indexService = new AppIndexService(dataDir);
        _updateService = new UpdateService();
        _runHistoryService = new RunHistoryService(dataDir);
        await _indexService.BuildIndexAsync();
        _indexService.StartWatching();

        // Plugins
        var plugins = new IQueryPlugin[]
        {
            new CalculatorPlugin(),
            new CurrencyPlugin(cacheService, TimeSpan.FromHours(_settings.CurrencyCacheTtlHours)),
            new SystemCommandPlugin(),
            new RunPlugin(_runHistoryService),
            new FileSearchPlugin(),
            new AppSearchPlugin(_indexService),
        };

        var router = new QueryRouter(plugins);
        var viewModel = new MainViewModel(router);

        // Main window
        _mainWindow = new MainWindow(viewModel);
        _updateService.StatusChanged += (_, args) =>
        {
            Current.Dispatcher.Invoke(() =>
            {
                UpdateTrayStatus(args.StatusText);
            });
        };

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
        if (!TryRegisterHotkey(_settings, out var errorMessage))
        {
            var defaultSettings = new WalkSettings();
            var fallbackApplied =
                (_settings.HotkeyModifiers != defaultSettings.HotkeyModifiers ||
                 _settings.HotkeyKey != defaultSettings.HotkeyKey) &&
                TryRegisterHotkey(defaultSettings, out _);

            if (fallbackApplied)
            {
                _settings.HotkeyModifiers = defaultSettings.HotkeyModifiers;
                _settings.HotkeyKey = defaultSettings.HotkeyKey;
                await _settingsService.SaveAsync(_settings);

                errorMessage = $"{errorMessage} Walk reverted to {HotkeyService.FormatDisplayText(_settings.HotkeyModifiers, _settings.HotkeyKey)}.";
            }

            System.Windows.MessageBox.Show(errorMessage, "Walk", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _hotkeyService.HotkeyPressed += () =>
        {
            Current.Dispatcher.Invoke(() =>
            {
                if (_settingsViewModel?.IsRecording == true)
                {
                    _settingsViewModel.ApplyRecordedHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey);
                    return;
                }

                viewModel.Toggle();
            });
        };
        _singleInstanceManager.StartListening(() =>
        {
            Current.Dispatcher.Invoke(() => viewModel.Show());
        });

        // Watch system theme
        Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();
        Wpf.Ui.Appearance.SystemThemeWatcher.Watch(_mainWindow);

        // Auto-start
        ConfigureAutoStart(_settings.StartWithWindows);

        // System tray
        SetupTray(viewModel, _updateService);
        _updateService.Start();
    }

    private void SetupTray(MainViewModel viewModel, UpdateService updateService)
    {
        var contextMenu = new Forms.ContextMenuStrip();
        _trayStatusItem = new Forms.ToolStripMenuItem(updateService.StatusText)
        {
            Enabled = false,
        };
        _checkForUpdatesItem = new Forms.ToolStripMenuItem("Check for Updates")
        {
            Enabled = updateService.CanCheckForUpdates,
        };
        _checkForUpdatesItem.Click += async (_, _) => await updateService.CheckForUpdatesAsync(manual: true);

        contextMenu.Items.Add(_trayStatusItem);
        contextMenu.Items.Add(_checkForUpdatesItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add("Show Launcher", null, (_, _) =>
            Current.Dispatcher.Invoke(() => viewModel.Show()));
        contextMenu.Items.Add("Settings", null, (_, _) =>
            Current.Dispatcher.Invoke(ShowSettings));
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add("Quit", null, (_, _) =>
            Current.Dispatcher.Invoke(() => Shutdown()));

        _trayDefaultIcon = TrayIconGenerator.Create(32);
        _trayActiveIcon = TrayIconGenerator.Create(32, active: true);

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _trayDefaultIcon,
            Text = BuildTrayTooltip(updateService.Version),
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

    private static string BuildTrayTooltip(string version)
    {
        var tooltip = $"Walk {AppVersionService.FormatVersionBadge(version)}";
        return tooltip.Length <= 63 ? tooltip : tooltip[..63];
    }

    private void UpdateTrayStatus(string statusText)
    {
        if (_trayStatusItem is not null)
            _trayStatusItem.Text = statusText;
    }

    private bool TryRegisterHotkey(WalkSettings settings, out string errorMessage)
    {
        if (_mainWindow is null || _hotkeyService is null)
        {
            errorMessage = "Walk could not initialize the global hotkey.";
            return false;
        }

        var handle = new WindowInteropHelper(_mainWindow).Handle;
        return _hotkeyService.Register(
            handle,
            settings.HotkeyModifiers,
            settings.HotkeyKey,
            out errorMessage);
    }

    private async void ShowSettings()
    {
        if (_settingsService is null)
            return;

        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var settingsViewModel = new SettingsViewModel(_settings, AppVersionService.GetDisplayVersion());
        var settingsWindow = new SettingsWindow(settingsViewModel);

        _settingsViewModel = settingsViewModel;
        _settingsWindow = settingsWindow;
        settingsWindow.Owner = _mainWindow;
        settingsWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(_settingsWindow, settingsWindow))
                _settingsWindow = null;

            if (ReferenceEquals(_settingsViewModel, settingsViewModel))
                _settingsViewModel = null;
        };

        var result = settingsWindow.ShowDialog();
        if (result != true)
            return;

        var updatedSettings = settingsViewModel.BuildSettings();
        var previousSettings = _settings.Clone();
        if (!TryRegisterHotkey(updatedSettings, out var errorMessage))
        {
            TryRegisterHotkey(previousSettings, out _);
            System.Windows.MessageBox.Show(
                $"{errorMessage} Walk kept {HotkeyService.FormatDisplayText(previousSettings.HotkeyModifiers, previousSettings.HotkeyKey)}.",
                "Walk",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _settings = updatedSettings;

        try
        {
            await _settingsService.SaveAsync(_settings);
            ConfigureAutoStart(_settings.StartWithWindows);
        }
        catch
        {
            System.Windows.MessageBox.Show(
                "Walk could not save settings to disk. Some changes may remain active until Walk restarts, but they were not persisted.",
                "Walk",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
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
        _singleInstanceManager?.Dispose();
        _hotkeyService?.Dispose();
        _indexService?.Dispose();
        _updateService?.Dispose();
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
