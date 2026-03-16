using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Walk.Models;

namespace Walk.Services;

public sealed class AppIndexService : IAppIndexService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private static readonly string[] SupportedLaunchExtensions =
    [
        ".appref-ms",
        ".bat",
        ".cmd",
        ".com",
        ".cpl",
        ".exe",
        ".msc",
        ".ps1",
    ];

    private const int StartAppsSourcePriority = 400;
    private const int ShortcutSourcePriority = 300;
    private const int AppPathSourcePriority = 200;
    private const int PathSourcePriority = 100;
    private const int ShortcutBufferCapacity = 32768;

    private readonly string _indexPath;
    private readonly AppIndexOptions _options;
    private readonly IStartAppProvider _startAppProvider;
    private readonly List<FileSystemWatcher> _watchers = [];
    private List<AppEntry> _entries = [];
    private CancellationTokenSource? _rebuildCts;

    public AppIndexService(
        string dataDir,
        AppIndexOptions? options = null,
        IStartAppProvider? startAppProvider = null)
    {
        _indexPath = Path.Combine(dataDir, "appindex.json");
        _options = options ?? new AppIndexOptions();
        _startAppProvider = startAppProvider ?? new PowerShellStartAppProvider();
    }

    public IReadOnlyList<AppEntry> Entries => _entries;

    public async Task BuildIndexAsync()
    {
        var entriesByIdentity = new Dictionary<string, IndexedEntry>(StringComparer.OrdinalIgnoreCase);

        await IndexStartAppEntriesAsync(entriesByIdentity);

        var entries = entriesByIdentity.Values
            .Select(static indexedEntry => indexedEntry.Entry)
            .OrderBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await MergeWithExistingIndex(entries);
        _entries = entries;
        await SaveIndexAsync();
    }

    public async Task RecordLaunchAsync(AppEntry entry)
    {
        var identity = BuildEntryIdentity(entry.ExecutablePath, entry.Arguments);
        var indexedEntry = _entries.FirstOrDefault(existing =>
            string.Equals(
                BuildEntryIdentity(existing.ExecutablePath, existing.Arguments),
                identity,
                StringComparison.OrdinalIgnoreCase));
        if (indexedEntry is null)
            return;

        indexedEntry.LaunchCount++;
        indexedEntry.LastUsed = DateTime.UtcNow;
        await SaveIndexAsync();
    }

    public void StartWatching()
    {
        foreach (var directory in _options.ShortcutDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory))
                continue;

            var watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
            watcher.Created += (_, args) =>
            {
                if (IsShortcutPath(args.FullPath))
                    ScheduleDebouncedRebuild();
            };
            watcher.Deleted += (_, args) =>
            {
                if (IsShortcutPath(args.FullPath))
                    ScheduleDebouncedRebuild();
            };
            watcher.Changed += (_, args) =>
            {
                if (IsShortcutPath(args.FullPath))
                    ScheduleDebouncedRebuild();
            };
            watcher.Renamed += (_, args) =>
            {
                if (IsShortcutPath(args.FullPath) || IsShortcutPath(args.OldFullPath))
                    ScheduleDebouncedRebuild();
            };
            _watchers.Add(watcher);
        }
    }

    public void Dispose()
    {
        _rebuildCts?.Cancel();
        _rebuildCts?.Dispose();
        foreach (var watcher in _watchers)
            watcher.Dispose();
    }

    private void IndexShortcutEntries(IDictionary<string, IndexedEntry> entriesByIdentity)
    {
        foreach (var directory in _options.ShortcutDirectories)
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (var shortcutPath in EnumerateFilesSafely(directory, "*.lnk"))
            {
                var entry = ResolveShortcut(shortcutPath);
                if (entry is not null)
                    AddOrMergeEntry(entriesByIdentity, entry, ShortcutSourcePriority);
            }

            foreach (var shortcutPath in EnumerateFilesSafely(directory, "*.url"))
            {
                var entry = ResolveInternetShortcut(shortcutPath);
                if (entry is not null)
                    AddOrMergeEntry(entriesByIdentity, entry, ShortcutSourcePriority);
            }
        }
    }

    private async Task IndexStartAppEntriesAsync(IDictionary<string, IndexedEntry> entriesByIdentity)
    {
        var startApps = await _startAppProvider.GetAppsAsync();
        foreach (var app in startApps)
        {
            if (string.IsNullOrWhiteSpace(app.Name) || string.IsNullOrWhiteSpace(app.AppId))
                continue;

            AddOrMergeEntry(
                entriesByIdentity,
                new AppEntry
                {
                    Name = app.Name.Trim(),
                    ExecutablePath = BuildStartAppLaunchTarget(app.AppId),
                },
                StartAppsSourcePriority);
        }
    }

    private void IndexAppPathEntries(IDictionary<string, IndexedEntry> entriesByIdentity)
    {
        foreach (var registryPath in _options.AppPathRegistryRoots)
        {
            using var rootKey = OpenRegistryPath(registryPath);
            if (rootKey is null)
                continue;

            foreach (var subKeyName in rootKey.GetSubKeyNames())
            {
                using var subKey = rootKey.OpenSubKey(subKeyName);
                if (subKey is null)
                    continue;

                var entry = ResolveAppPathEntry(subKeyName, subKey);
                if (entry is not null)
                    AddOrMergeEntry(entriesByIdentity, entry, AppPathSourcePriority);
            }
        }
    }

    private void IndexPathEntries(IDictionary<string, IndexedEntry> entriesByIdentity)
    {
        foreach (var directory in _options.PathDirectories)
        {
            if (!Directory.Exists(directory))
                continue;

            try
            {
                foreach (var executablePath in Directory.EnumerateFiles(directory, "*.exe"))
                {
                    AddOrMergeEntry(
                        entriesByIdentity,
                        new AppEntry
                        {
                            Name = Path.GetFileNameWithoutExtension(executablePath),
                            ExecutablePath = executablePath,
                            RevealPath = executablePath,
                        },
                        PathSourcePriority);
                }
            }
            catch
            {
                // Skip inaccessible directories.
            }
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
            catch (OperationCanceledException)
            {
            }
        });
    }

    private static void AddOrMergeEntry(
        IDictionary<string, IndexedEntry> entriesByIdentity,
        AppEntry incomingEntry,
        int incomingPriority)
    {
        incomingEntry = CloneEntry(incomingEntry, incomingPriority);
        var identity = BuildEntryIdentity(incomingEntry.ExecutablePath, incomingEntry.Arguments);
        if (!entriesByIdentity.TryGetValue(identity, out var existingEntry))
        {
            entriesByIdentity.Add(identity, new IndexedEntry(incomingEntry, incomingPriority));
            return;
        }

        entriesByIdentity[identity] = new IndexedEntry(
            MergeEntries(existingEntry.Entry, existingEntry.SourcePriority, incomingEntry, incomingPriority),
            Math.Max(existingEntry.SourcePriority, incomingPriority));
    }

    private static AppEntry MergeEntries(
        AppEntry existingEntry,
        int existingPriority,
        AppEntry incomingEntry,
        int incomingPriority)
    {
        var preferredEntry = incomingPriority > existingPriority ? incomingEntry : existingEntry;
        var fallbackEntry = ReferenceEquals(preferredEntry, existingEntry) ? incomingEntry : existingEntry;
        var name = SelectPreferredName(existingEntry.Name, existingPriority, incomingEntry.Name, incomingPriority);
        var iconPath = !string.IsNullOrWhiteSpace(preferredEntry.IconPath)
            ? preferredEntry.IconPath
            : fallbackEntry.IconPath;
        var iconIndex = !string.IsNullOrWhiteSpace(preferredEntry.IconPath)
            ? preferredEntry.IconIndex
            : fallbackEntry.IconIndex;

        return new AppEntry
        {
            Name = name,
            ExecutablePath = preferredEntry.ExecutablePath,
            SourcePriority = Math.Max(existingEntry.SourcePriority, incomingEntry.SourcePriority),
            Arguments = preferredEntry.Arguments ?? fallbackEntry.Arguments,
            WorkingDirectory = preferredEntry.WorkingDirectory ?? fallbackEntry.WorkingDirectory,
            RevealPath = preferredEntry.RevealPath ?? fallbackEntry.RevealPath,
            IconPath = iconPath,
            IconIndex = iconPath is null ? 0 : iconIndex,
            LaunchCount = existingEntry.LaunchCount,
            LastUsed = existingEntry.LastUsed,
        };
    }

    private static string SelectPreferredName(
        string existingName,
        int existingPriority,
        string incomingName,
        int incomingPriority)
    {
        if (incomingPriority > existingPriority)
            return incomingName;

        if (incomingPriority < existingPriority)
            return existingName;

        return incomingName.Length > existingName.Length ? incomingName : existingName;
    }

    private static AppEntry CloneEntry(AppEntry entry, int sourcePriority)
    {
        return new AppEntry
        {
            Name = entry.Name,
            ExecutablePath = entry.ExecutablePath,
            SourcePriority = sourcePriority,
            IconPath = entry.IconPath,
            IconIndex = entry.IconIndex,
            Arguments = entry.Arguments,
            WorkingDirectory = entry.WorkingDirectory,
            RevealPath = entry.RevealPath,
            LaunchCount = entry.LaunchCount,
            LastUsed = entry.LastUsed,
        };
    }

    private static IEnumerable<string> EnumerateFilesSafely(string directory, string filter)
    {
        try
        {
            return Directory.EnumerateFiles(directory, filter, SearchOption.AllDirectories);
        }
        catch
        {
            return [];
        }
    }

    private static AppEntry? ResolveShortcut(string shortcutPath)
    {
        try
        {
            var link = (IShellLinkW)new ShellLink();
            var file = (IPersistFile)link;
            file.Load(shortcutPath, 0);

            var targetPathBuffer = new StringBuilder(ShortcutBufferCapacity);
            link.GetPath(targetPathBuffer, targetPathBuffer.Capacity, IntPtr.Zero, 0);
            var targetPath = ResolvePathValue(targetPathBuffer.ToString());

            if (string.IsNullOrWhiteSpace(targetPath) ||
                !File.Exists(targetPath) ||
                !IsSupportedLaunchTarget(targetPath))
            {
                return null;
            }

            var argumentsBuffer = new StringBuilder(ShortcutBufferCapacity);
            link.GetArguments(argumentsBuffer, argumentsBuffer.Capacity);
            var arguments = NormalizeValue(argumentsBuffer.ToString());

            var workingDirectoryBuffer = new StringBuilder(ShortcutBufferCapacity);
            link.GetWorkingDirectory(workingDirectoryBuffer, workingDirectoryBuffer.Capacity);
            var workingDirectory = ResolvePathValue(workingDirectoryBuffer.ToString());
            if (!Directory.Exists(workingDirectory))
                workingDirectory = null;

            var iconLocation = GetShortcutIconLocation(link);

            return new AppEntry
            {
                Name = Path.GetFileNameWithoutExtension(shortcutPath),
                ExecutablePath = targetPath,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RevealPath = targetPath,
                IconPath = iconLocation.Path,
                IconIndex = iconLocation.Index,
            };
        }
        catch
        {
            return null;
        }
    }

    private static (string? Path, int Index) GetShortcutIconLocation(IShellLinkW link)
    {
        var iconLocationBuffer = new StringBuilder(ShortcutBufferCapacity);
        link.GetIconLocation(iconLocationBuffer, iconLocationBuffer.Capacity, out var iconIndex);
        var iconPath = ResolvePathValue(iconLocationBuffer.ToString());
        if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
            return (null, 0);

        return (iconPath, iconIndex);
    }

    private static AppEntry? ResolveInternetShortcut(string shortcutPath)
    {
        try
        {
            var shortcutValues = ParseInternetShortcut(shortcutPath);
            if (!shortcutValues.TryGetValue("URL", out var launchUrl) || !IsAppShortcutUrl(launchUrl))
                return null;

            var iconPath = shortcutValues.TryGetValue("IconFile", out var iconFile)
                ? ResolvePathValue(iconFile)
                : null;
            if (!File.Exists(iconPath))
                iconPath = null;

            var iconIndex = 0;
            if (shortcutValues.TryGetValue("IconIndex", out var iconIndexValue))
                int.TryParse(iconIndexValue, out iconIndex);

            return new AppEntry
            {
                Name = Path.GetFileNameWithoutExtension(shortcutPath),
                ExecutablePath = launchUrl.Trim(),
                IconPath = iconPath,
                IconIndex = iconPath is null ? 0 : iconIndex,
            };
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string> ParseInternetShortcut(string shortcutPath)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inInternetShortcutSection = false;

        foreach (var rawLine in File.ReadLines(shortcutPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inInternetShortcutSection = line.Equals("[InternetShortcut]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inInternetShortcutSection)
                continue;

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
                continue;

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            values[key] = value;
        }

        return values;
    }

    private static bool IsAppShortcutUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return false;

        if (uri.IsFile)
            return false;

        if (uri.Scheme is "http" or "https")
            return false;

        return !url.Contains("openurl", StringComparison.OrdinalIgnoreCase);
    }

    private static AppEntry? ResolveAppPathEntry(string keyName, RegistryKey subKey)
    {
        var rawCommand = subKey.GetValue(null) as string;
        if (!TryParseLaunchCommand(rawCommand, out var launchTarget, out var arguments))
            return null;

        launchTarget = ResolvePathValue(launchTarget);
        if (string.IsNullOrWhiteSpace(launchTarget) ||
            !File.Exists(launchTarget) ||
            !IsSupportedLaunchTarget(launchTarget))
        {
            return null;
        }

        var workingDirectory = ResolvePathValue(subKey.GetValue("Path") as string);
        if (!Directory.Exists(workingDirectory))
            workingDirectory = null;

        return new AppEntry
        {
            Name = GetFriendlyAppName(launchTarget, Path.GetFileNameWithoutExtension(keyName)),
            ExecutablePath = launchTarget,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RevealPath = launchTarget,
        };
    }

    private static string GetFriendlyAppName(string executablePath, string fallbackName)
    {
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
            if (!string.IsNullOrWhiteSpace(versionInfo.FileDescription))
                return versionInfo.FileDescription.Trim();
        }
        catch
        {
        }

        return fallbackName;
    }

    private static bool TryParseLaunchCommand(string? command, out string launchTarget, out string? arguments)
    {
        launchTarget = "";
        arguments = null;

        if (string.IsNullOrWhiteSpace(command))
            return false;

        var trimmedCommand = Environment.ExpandEnvironmentVariables(command.Trim());
        if (trimmedCommand.StartsWith('"'))
        {
            var closingQuoteIndex = trimmedCommand.IndexOf('"', 1);
            if (closingQuoteIndex <= 1)
                return false;

            launchTarget = trimmedCommand[1..closingQuoteIndex].Trim();
            var remainingArguments = trimmedCommand[(closingQuoteIndex + 1)..].Trim();
            arguments = NormalizeValue(remainingArguments);
            return launchTarget.Length > 0;
        }

        foreach (var extension in SupportedLaunchExtensions)
        {
            var extensionIndex = trimmedCommand.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
            if (extensionIndex < 0)
                continue;

            var targetEndIndex = extensionIndex + extension.Length;
            launchTarget = trimmedCommand[..targetEndIndex].Trim();
            var remainingArguments = trimmedCommand[targetEndIndex..].Trim();
            arguments = NormalizeValue(remainingArguments);
            return launchTarget.Length > 0;
        }

        launchTarget = trimmedCommand;
        return launchTarget.Length > 0;
    }

    private static RegistryKey? OpenRegistryPath(string registryPath)
    {
        if (string.IsNullOrWhiteSpace(registryPath))
            return null;

        var normalizedPath = registryPath.Replace('/', '\\');
        var separatorIndex = normalizedPath.IndexOf('\\');
        var hiveName = separatorIndex < 0 ? normalizedPath : normalizedPath[..separatorIndex];
        var subKeyPath = separatorIndex < 0 ? "" : normalizedPath[(separatorIndex + 1)..];

        return hiveName.ToUpperInvariant() switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser.OpenSubKey(subKeyPath),
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine.OpenSubKey(subKeyPath),
            _ => null,
        };
    }

    private async Task MergeWithExistingIndex(List<AppEntry> newEntries)
    {
        if (!File.Exists(_indexPath))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(_indexPath);
            var existingEntries = JsonSerializer.Deserialize<List<AppEntry>>(json) ?? [];
            var existingByIdentity = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var existingEntry in existingEntries)
            {
                existingByIdentity.TryAdd(
                    BuildEntryIdentity(existingEntry.ExecutablePath, existingEntry.Arguments),
                    existingEntry);
            }

            foreach (var newEntry in newEntries)
            {
                if (!existingByIdentity.TryGetValue(
                        BuildEntryIdentity(newEntry.ExecutablePath, newEntry.Arguments),
                        out var existingEntry))
                {
                    continue;
                }

                newEntry.LaunchCount = existingEntry.LaunchCount;
                newEntry.LastUsed = existingEntry.LastUsed;
            }
        }
        catch
        {
            // Ignore a corrupted index and rebuild from scratch.
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
            // Ignore save errors.
        }
    }

    private static string BuildEntryIdentity(string executablePath, string? arguments)
    {
        return $"{executablePath.Trim()}\n{NormalizeValue(arguments) ?? ""}";
    }

    private static string BuildStartAppLaunchTarget(string appId)
    {
        return $@"shell:AppsFolder\{appId.Trim()}";
    }

    private static string? ResolvePathValue(string? value)
    {
        var normalizedValue = NormalizeValue(value);
        return normalizedValue is null ? null : Environment.ExpandEnvironmentVariables(normalizedValue);
    }

    private static string? NormalizeValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsSupportedLaunchTarget(string targetPath)
    {
        var extension = Path.GetExtension(targetPath);
        return SupportedLaunchExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsShortcutPath(string path)
    {
        return path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".url", StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct IndexedEntry(AppEntry Entry, int SourcePriority);

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
