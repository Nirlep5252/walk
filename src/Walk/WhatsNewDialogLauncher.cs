using System.IO;
using Walk.Services;

namespace Walk;

public static class WhatsNewDialogLauncher
{
    public static void Run()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Walk");
        var errorLogPath = Path.Combine(dataDir, "whats-new-launcher-error.log");

        try
        {
            var changelogService = new ChangelogService(dataDir);
            var changelogRecoveryService = new ChangelogRecoveryService(changelogService, new ReleaseNotesService());
            var version = AppVersionService.GetDisplayVersion();
            var entry = changelogRecoveryService.GetLatestAvailableForVersionAsync(version).GetAwaiter().GetResult();

            var app = new System.Windows.Application
            {
                ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown,
            };
            app.DispatcherUnhandledException += (_, args) =>
            {
                Directory.CreateDirectory(dataDir);
                File.WriteAllText(errorLogPath, args.Exception.ToString());
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                Directory.CreateDirectory(dataDir);
                if (args.ExceptionObject is Exception ex)
                    File.WriteAllText(errorLogPath, ex.ToString());
            };

            app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary
            {
                Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark,
            });
            app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());

            Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();

            var window = entry is null
                ? new WhatsNewWindow()
                : new WhatsNewWindow(entry, mandatory: false);

            window.ShowInTaskbar = true;
            window.Closed += (_, _) => app.Shutdown();

            app.Startup += (_, _) =>
            {
                app.MainWindow = window;
                window.Show();
                window.Activate();
            };

            app.Run();
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(errorLogPath, ex.ToString());
            throw;
        }
    }
}
