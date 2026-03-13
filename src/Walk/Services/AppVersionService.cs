using System.Reflection;

namespace Walk.Services;

public static class AppVersionService
{
    public static string GetDisplayVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return Normalize(informationalVersion, assembly.GetName().Version);
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
}
