using System.IO;

namespace Walk.Services;

public sealed class AppIndexOptions
{
    public IReadOnlyList<string> ShortcutDirectories { get; init; } = BuildDefaultShortcutDirectories();
    public IReadOnlyList<string> PathDirectories { get; init; } = BuildDefaultPathDirectories();
    public IReadOnlyList<string> AppPathRegistryRoots { get; init; } =
    [
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths",
        @"HKLM\Software\Microsoft\Windows\CurrentVersion\App Paths",
        @"HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths",
    ];

    private static string[] BuildDefaultShortcutDirectories()
    {
        return
        [
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Start Menu\Programs"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                @"Microsoft\Windows\Start Menu\Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        ];
    }

    private static string[] BuildDefaultPathDirectories()
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Directory.Exists(directory))
                directories.Add(directory);
        }

        return directories.ToArray();
    }
}
