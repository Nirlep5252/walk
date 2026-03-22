using System.Diagnostics;
using System.IO;
using System.Text;

namespace Walk.Services;

public sealed class EverythingBundledRuntime : IDisposable
{
    private readonly string _dataDir;
    private Process? _process;
    private bool _ownsProcess;

    public EverythingBundledRuntime(string appDataDir)
    {
        _dataDir = Path.Combine(appDataDir, "Everything");
    }

    public string ExecutablePath => Path.Combine(AppContext.BaseDirectory, "Everything.exe");

    public bool IsAvailable => File.Exists(ExecutablePath);

    public void EnsureStarted()
    {
        if (!IsAvailable)
            return;

        if (_process is { HasExited: false })
            return;

        Directory.CreateDirectory(_dataDir);
        WriteConfig();

        try
        {
            _process = Process.Start(new ProcessStartInfo
            {
                FileName = ExecutablePath,
                Arguments = BuildArguments(),
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(ExecutablePath) ?? AppContext.BaseDirectory,
            });
            _ownsProcess = _process is not null;
        }
        catch
        {
            _process = null;
            _ownsProcess = false;
        }
    }

    public void Dispose()
    {
        if (!_ownsProcess)
            return;

        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
        finally
        {
            _process?.Dispose();
            _process = null;
            _ownsProcess = false;
        }
    }

    private string BuildArguments()
    {
        var configPath = Path.Combine(_dataDir, "Everything.ini");
        var databasePath = Path.Combine(_dataDir, "Everything.db");
        return $"-startup -config \"{configPath}\" -db \"{databasePath}\" -rescan-all";
    }

    private void WriteConfig()
    {
        var configPath = Path.Combine(_dataDir, "Everything.ini");
        File.WriteAllText(configPath, BuildConfigContent(GetIndexedFolders()));
    }

    public static string BuildConfigContent(IReadOnlyList<string> indexedFolders)
    {
        var config = new StringBuilder(
            """
[Everything]
app_data=0
run_as_admin=0
run_in_background=1
show_tray_icon=0
check_for_updates_on_startup=0
search_history_enabled=0
run_history_enabled=0
folder_update_rescan_asap=1
"""
                .ReplaceLineEndings(Environment.NewLine));

        if (indexedFolders.Count > 0)
        {
            config.Append("folders=").AppendJoin(',', indexedFolders).AppendLine();
            config.Append("folder_monitor_changes=").AppendJoin(',', indexedFolders.Select(static _ => "1")).AppendLine();
            config.Append("folder_rescan_if_full_list=").AppendJoin(',', indexedFolders.Select(static _ => "1")).AppendLine();
            config.Append("folder_update_types=").AppendJoin(',', indexedFolders.Select(static _ => "1")).AppendLine();
            config.Append("folder_update_days=").AppendJoin(',', indexedFolders.Select(static _ => "0")).AppendLine();
            config.Append("folder_update_ats=").AppendJoin(',', indexedFolders.Select(static _ => "0")).AppendLine();
            config.Append("folder_update_intervals=").AppendJoin(',', indexedFolders.Select(static _ => "15")).AppendLine();
            config.Append("folder_update_interval_types=").AppendJoin(',', indexedFolders.Select(static _ => "0")).AppendLine();
            config.Append("folder_buffer_size_list=").AppendJoin(',', indexedFolders.Select(static _ => "65536")).AppendLine();
        }

        return config.ToString();
    }

    public static IReadOnlyList<string> GetIndexedFolders()
    {
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            GetDownloadsFolder(),
        };

        return folders
            .Where(static folder => !string.IsNullOrWhiteSpace(folder))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(EscapeFolderValue)
            .ToList();
    }

    private static string GetDownloadsFolder()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(userProfile)
            ? ""
            : Path.Combine(userProfile, "Downloads");
    }

    private static string EscapeFolderValue(string folder)
    {
        return folder.Contains(',')
            ? $"\"{folder}\""
            : folder;
    }
}
