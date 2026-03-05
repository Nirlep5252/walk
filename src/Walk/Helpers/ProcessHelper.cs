using System.Diagnostics;
using System.IO;

namespace Walk.Helpers;

public static class ProcessHelper
{
    public static void Launch(string path, bool asAdmin, string? arguments = null, string? workingDirectory = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            Arguments = arguments ?? "",
            UseShellExecute = true,
        };

        var effectiveWorkingDirectory = ResolveWorkingDirectory(path, workingDirectory);
        if (!string.IsNullOrWhiteSpace(effectiveWorkingDirectory))
            startInfo.WorkingDirectory = effectiveWorkingDirectory;

        if (asAdmin)
            startInfo.Verb = "runas";

        Process.Start(startInfo);
    }

    public static void OpenFileLocation(string filePath)
    {
        Process.Start("explorer.exe", $"/select,\"{filePath}\"");
    }

    private static string? ResolveWorkingDirectory(string path, string? configuredWorkingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredWorkingDirectory) &&
            Directory.Exists(configuredWorkingDirectory))
        {
            return configuredWorkingDirectory!;
        }

        var executableDirectory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(executableDirectory) &&
            Directory.Exists(executableDirectory))
        {
            return executableDirectory!;
        }

        return null;
    }
}
