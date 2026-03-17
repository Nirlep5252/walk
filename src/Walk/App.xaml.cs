using System.IO;
using System.Diagnostics;
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
    private Forms.ToolStripMenuItem? _whatsNewItem;
    private System.Drawing.Icon? _trayDefaultIcon;
    private System.Drawing.Icon? _trayActiveIcon;
    private AppIndexService? _indexService;
    private CacheService? _cacheService;
    private UpdateService? _updateService;
    private ChangelogService? _changelogService;
    private ChangelogRecoveryService? _changelogRecoveryService;
    private SettingsService? _settingsService;
    private RunHistoryService? _runHistoryService;
    private QueryRouter? _router;
    private WalkSettings _settings = new();
    private SettingsWindow? _settingsWindow;
    private SettingsViewModel? _settingsViewModel;
    private WhatsNewWindow? _whatsNewWindow;

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
        _cacheService = new CacheService(dataDir);
        _indexService = new AppIndexService(dataDir);
        _changelogService = new ChangelogService(dataDir);
        _changelogRecoveryService = new ChangelogRecoveryService(_changelogService, new ReleaseNotesService());
        _updateService = new UpdateService(_changelogService);
        _runHistoryService = new RunHistoryService(dataDir);
        await _indexService.BuildIndexAsync();
        _indexService.StartWatching();

        _router = new QueryRouter(BuildPlugins(_settings));
        var viewModel = new MainViewModel(_router, _settings.MaxResults);

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

        await _changelogRecoveryService.EnsureCurrentVersionPendingAsync(_updateService.Version);

        // System tray
        SetupTray(viewModel, _updateService);
        await ShowPendingChangelogAsync();
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
        _whatsNewItem = new Forms.ToolStripMenuItem("What's New", null, (_, _) => LaunchWhatsNewDialogProcess());

        contextMenu.Items.Add(_trayStatusItem);
        contextMenu.Items.Add(_checkForUpdatesItem);
        contextMenu.Items.Add(_whatsNewItem);
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

    private async Task ShowPendingChangelogAsync()
    {
        if (_changelogService is null || _updateService is null)
            return;

        var entry = await _changelogService.GetPendingAsync(_updateService.Version);
        if (entry is null)
            return;

        ShowChangelogWindow(entry, mandatory: true);
        await _changelogService.MarkPendingAsSeenAsync(entry.Version);
    }

    private async Task ShowLatestChangelogAsync()
    {
        if (_changelogRecoveryService is null || _updateService is null)
            return;

        var entry = await _changelogRecoveryService.GetLatestAvailableForVersionAsync(_updateService.Version);
        if (entry is null)
        {
            ShowWhatsNewWindow(new WhatsNewWindow(), modal: false);
            return;
        }

        ShowWhatsNewWindow(new WhatsNewWindow(entry, mandatory: false), modal: false);
    }

    private void ShowChangelogWindow(ChangelogEntry entry, bool mandatory)
    {
        ShowWhatsNewWindow(new WhatsNewWindow(entry, mandatory), modal: mandatory, forceFront: mandatory);
    }

    private void LaunchWhatsNewDialogProcess()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--show-whats-new",
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory,
            });
        }
        catch
        {
            Current.Dispatcher.Invoke(() => _ = ShowLatestChangelogAsync());
        }
    }

    private void ShowWhatsNewWindow(WhatsNewWindow window, bool modal, bool forceFront = false, bool showInTaskbar = false)
    {
        if (!modal && _whatsNewWindow is not null)
        {
            _whatsNewWindow.ShowInTaskbar = showInTaskbar;
            _whatsNewWindow.Show();
            _whatsNewWindow.WindowState = WindowState.Normal;
            _whatsNewWindow.Activate();
            _whatsNewWindow.Focus();
            return;
        }

        if (_mainWindow?.IsVisible == true)
            window.Owner = _mainWindow;

        window.ShowInTaskbar = showInTaskbar;

        if (modal)
        {
            if (forceFront)
            {
                window.Topmost = true;
                window.Loaded += (_, _) =>
                {
                    window.Activate();
                    window.Focus();
                };
            }

            window.ShowDialog();
            return;
        }

        _whatsNewWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_whatsNewWindow, window))
                _whatsNewWindow = null;
        };

        window.Show();
        window.Activate();
        window.Focus();
        if (forceFront)
        {
            window.Topmost = true;
            window.Topmost = false;
        }
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
        _router?.UpdatePlugins(BuildPlugins(_settings));

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

    private IReadOnlyList<IQueryPlugin> BuildPlugins(WalkSettings settings)
    {
        if (_cacheService is null || _runHistoryService is null || _indexService is null)
            return [];

        var plugins = new List<IQueryPlugin>();

        if (settings.EnableCalculator)
            plugins.Add(new CalculatorPlugin());

        if (settings.EnableCurrencyConverter)
        {
            plugins.Add(new CurrencyPlugin(
                _cacheService,
                TimeSpan.FromHours(settings.CurrencyCacheTtlHours)));
        }

        if (settings.EnableSystemCommands)
            plugins.Add(new SystemCommandPlugin());

        if (settings.EnableRunner)
            plugins.Add(new RunPlugin(_runHistoryService));

        if (settings.EnableFileSearch)
            plugins.Add(new FileSearchPlugin());

        plugins.Add(new AppSearchPlugin(_indexService));
        return plugins;
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
