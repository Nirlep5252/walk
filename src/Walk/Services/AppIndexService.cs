using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;
using Walk.Models;

namespace Walk.Services;

public sealed class AppIndexService : IDisposable
{
    private readonly string _indexPath;
    private List<AppEntry> _entries = [];
    private readonly List<FileSystemWatcher> _watchers = [];
    private CancellationTokenSource? _rebuildCts;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private static readonly string[] StartMenuPaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Start Menu\Programs"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            @"Microsoft\Windows\Start Menu\Programs"),
    ];

    public AppIndexService(string dataDir)
    {
        _indexPath = Path.Combine(dataDir, "appindex.json");
    }

    public IReadOnlyList<AppEntry> Entries => _entries;

    public async Task BuildIndexAsync()
    {
        var entries = new List<AppEntry>();

        foreach (var dir in StartMenuPaths)
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories))
            {
                var entry = ResolveShortcut(lnk);
                if (entry is not null)
                    entries.Add(entry);
            }
        }

        // Index PATH executables — use HashSet for O(1) dedup
        var knownNames = new HashSet<string>(entries.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Directory.Exists(dir))
                continue;

            try
            {
                foreach (var exe in Directory.EnumerateFiles(dir, "*.exe"))
                {
                    var name = Path.GetFileNameWithoutExtension(exe);
                    if (knownNames.Add(name))
                    {
                        entries.Add(new AppEntry
                        {
                            Name = name,
                            ExecutablePath = exe,
                        });
                    }
                }
            }
            catch
            {
                // Skip inaccessible directories
            }
        }

        await MergeWithExistingIndex(entries);
        _entries = entries;
        await SaveIndexAsync();
    }

    public async Task RecordLaunchAsync(string executablePath)
    {
        var entry = _entries.FirstOrDefault(e =>
            e.ExecutablePath.Equals(executablePath, StringComparison.OrdinalIgnoreCase));
        if (entry is not null)
        {
            entry.LaunchCount++;
            entry.LastUsed = DateTime.UtcNow;
            await SaveIndexAsync();
        }
    }

    public void StartWatching()
    {
        foreach (var dir in StartMenuPaths)
        {
            if (!Directory.Exists(dir))
                continue;

            var watcher = new FileSystemWatcher(dir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };
            watcher.Created += (_, _) => ScheduleDebouncedRebuild();
            watcher.Deleted += (_, _) => ScheduleDebouncedRebuild();
            _watchers.Add(watcher);
        }
    }

    private void ScheduleDebouncedRebuild()
    {
        _rebuildCts?.Cancel();
        _rebuildCts = new CancellationTokenSource();
        var token = _rebuildCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2000, token);
                await BuildIndexAsync();
            }
            catch (OperationCanceledException) { }
        });
    }

    public void Dispose()
    {
        _rebuildCts?.Cancel();
        _rebuildCts?.Dispose();
        foreach (var w in _watchers)
            w.Dispose();
    }

    private static AppEntry? ResolveShortcut(string lnkPath)
    {
        try
        {
            var link = (IShellLinkW)new ShellLink();
            var file = (IPersistFile)link;
            file.Load(lnkPath, 0);

            var sb = new StringBuilder(260);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            var targetPath = sb.ToString();

            if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
                return null;

            // Skip non-exe targets
            if (!targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return null;

            sb.Clear();
            link.GetArguments(sb, sb.Capacity);
            var arguments = sb.ToString();

            sb.Clear();
            link.GetWorkingDirectory(sb, sb.Capacity);
            var workingDirectory = sb.ToString();

            return new AppEntry
            {
                Name = Path.GetFileNameWithoutExtension(lnkPath),
                ExecutablePath = targetPath,
                Arguments = string.IsNullOrEmpty(arguments) ? null : arguments,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory,
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task MergeWithExistingIndex(List<AppEntry> newEntries)
    {
        if (!File.Exists(_indexPath))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(_indexPath);
            var existing = JsonSerializer.Deserialize<List<AppEntry>>(json) ?? [];

            // Use Dictionary for O(1) lookup instead of O(n) FirstOrDefault per entry
            var existingByPath = new Dictionary<string, AppEntry>(existing.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var e in existing)
                existingByPath.TryAdd(e.ExecutablePath, e);

            foreach (var newEntry in newEntries)
            {
                if (existingByPath.TryGetValue(newEntry.ExecutablePath, out var old))
                {
                    newEntry.LaunchCount = old.LaunchCount;
                    newEntry.LastUsed = old.LastUsed;
                }
            }
        }
        catch
        {
            // Corrupted index — start fresh
        }
    }

    private async Task SaveIndexAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_entries, JsonOptions);
            await File.WriteAllTextAsync(_indexPath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }

    // COM interop for resolving .lnk shortcuts
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
