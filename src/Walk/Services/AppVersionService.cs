using System.IO;
using System.Reflection;

namespace Walk.Services;

public static class AppVersionService
{
    public const string DevelopmentModeVersion = "dev";
    public const string DevelopmentModeLabel = "Dev Mode";

    public static string GetDisplayVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        var version = Normalize(informationalVersion, assembly.GetName().Version);
        return IsDevelopmentBuild(AppContext.BaseDirectory) ? DevelopmentModeVersion : version;
    }

    public static string Normalize(string? informationalVersion, Version? fallbackVersion = null)
    {
        var normalized = informationalVersion?
            .Split('+', 2, StringSplitOptions.TrimEntries)[0]
            .Trim();

        if (!string.IsNullOrWhiteSpace(normalized))
            return normalized;

        return fallbackVersion is null
            ? "0.0.0"
            : $"{fallbackVersion.Major}.{Math.Max(fallbackVersion.Minor, 0)}.{Math.Max(fallbackVersion.Build, 0)}";
    }

    public static bool IsDevelopmentBuild(string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            return false;

        var fullPath = Path.GetFullPath(baseDirectory);
        if (!LooksLikeBuildOutput(fullPath))
            return false;

        for (var current = new DirectoryInfo(fullPath); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "Walk.sln")) ||
                Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return true;
            }
        }

        return false;
    }

    public static string FormatVersionBadge(string version)
    {
        return string.Equals(version, DevelopmentModeVersion, StringComparison.OrdinalIgnoreCase)
            ? DevelopmentModeLabel
            : $"v{version}";
    }

    public static string FormatSettingsLabel(string version)
    {
        return string.Equals(version, DevelopmentModeVersion, StringComparison.OrdinalIgnoreCase)
            ? DevelopmentModeLabel
            : $"Version {version}";
    }

    private static bool LooksLikeBuildOutput(string fullPath)
    {
        return fullPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               fullPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }
}
