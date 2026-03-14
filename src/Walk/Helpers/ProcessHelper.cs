using System.Diagnostics;
using System.IO;

namespace Walk.Helpers;

public static class ProcessHelper
{
    public static void Launch(string path, bool asAdmin, string? arguments = null, string? workingDirectory = null)
    {
        var expandedPath = ExpandValue(path);
        var startInfo = new ProcessStartInfo
        {
            FileName = expandedPath,
            Arguments = arguments ?? "",
            UseShellExecute = true,
        };

        if (!IsShellUri(expandedPath))
        {
            var effectiveWorkingDirectory = ResolveWorkingDirectory(expandedPath, workingDirectory);
            if (!string.IsNullOrWhiteSpace(effectiveWorkingDirectory))
                startInfo.WorkingDirectory = effectiveWorkingDirectory;

            if (asAdmin)
                startInfo.Verb = "runas";
        }

        Process.Start(startInfo);
    }

    public static void OpenFileLocation(string filePath)
    {
        var expandedPath = ExpandValue(filePath);
        if (Directory.Exists(expandedPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = expandedPath,
                UseShellExecute = true,
            });
            return;
        }

        Process.Start("explorer.exe", $"/select,\"{expandedPath}\"");
    }

    private static string? ResolveWorkingDirectory(string path, string? configuredWorkingDirectory)
    {
        var expandedConfiguredWorkingDirectory = ExpandValue(configuredWorkingDirectory);
        if (!string.IsNullOrWhiteSpace(expandedConfiguredWorkingDirectory) &&
            Directory.Exists(expandedConfiguredWorkingDirectory))
        {
            return expandedConfiguredWorkingDirectory!;
        }

        var executableDirectory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(executableDirectory) &&
            Directory.Exists(executableDirectory))
        {
            return executableDirectory!;
        }

        return null;
    }

    private static string ExpandValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ""
            : Environment.ExpandEnvironmentVariables(value.Trim());
    }

    private static bool IsShellUri(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsFile;
    }
}
