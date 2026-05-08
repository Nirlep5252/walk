using System.IO;
using Walk.Models;

namespace Walk.Helpers;

public static class RunTargetWorkingDirectory
{
    public static string? Resolve(RunTarget target)
    {
        var configuredWorkingDirectory = ExpandValue(target.WorkingDirectory);
        if (!string.IsNullOrWhiteSpace(configuredWorkingDirectory) &&
            Directory.Exists(configuredWorkingDirectory))
        {
            return configuredWorkingDirectory;
        }

        return GetDefaultDirectoryForCommand(target.Command);
    }

    public static string? GetDefaultPowerShellDirectory()
    {
        return GetUserProfileDirectory();
    }

    public static string? GetDefaultDirectoryForCommand(string command)
    {
        return UsesPowerShellDefaultDirectory(command)
            ? GetDefaultPowerShellDirectory()
            : null;
    }

    private static bool UsesPowerShellDefaultDirectory(string command)
    {
        var executableName = GetExecutableName(command);
        return executableName.Equals("powershell", StringComparison.OrdinalIgnoreCase) ||
               executableName.Equals("pwsh", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetExecutableName(string command)
    {
        var executable = GetFirstCommandToken(command);
        var fileName = Path.GetFileName(executable);
        return Path.GetFileNameWithoutExtension(fileName);
    }

    private static string GetFirstCommandToken(string command)
    {
        var trimmed = command.Trim();
        if (trimmed.Length == 0)
            return "";

        if (trimmed[0] == '"')
        {
            var closingQuoteIndex = trimmed.IndexOf('"', 1);
            return closingQuoteIndex > 1
                ? trimmed[1..closingQuoteIndex]
                : trimmed.Trim('"');
        }

        var whitespaceIndex = trimmed.IndexOfAny([' ', '\t']);
        return whitespaceIndex > 0
            ? trimmed[..whitespaceIndex]
            : trimmed;
    }

    private static string? GetUserProfileDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile) && Directory.Exists(userProfile))
            return userProfile;

        var expandedUserProfile = Environment.ExpandEnvironmentVariables("%USERPROFILE%");
        return !string.IsNullOrWhiteSpace(expandedUserProfile) && Directory.Exists(expandedUserProfile)
            ? expandedUserProfile
            : null;
    }

    private static string? ExpandValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : Environment.ExpandEnvironmentVariables(value.Trim());
    }
}
