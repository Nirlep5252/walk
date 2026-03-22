using System.Diagnostics;
using System.IO;

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
        return $"-startup -config \"{configPath}\" -db \"{databasePath}\"";
    }

    private void WriteConfig()
    {
        var configPath = Path.Combine(_dataDir, "Everything.ini");
        var config = """
[Everything]
app_data=0
run_as_admin=0
run_in_background=1
show_tray_icon=0
check_for_updates_on_startup=0
search_history_enabled=0
run_history_enabled=0
"""
            .ReplaceLineEndings(Environment.NewLine);

        File.WriteAllText(configPath, config);
    }
}
