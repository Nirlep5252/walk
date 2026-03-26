using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Walk.Helpers;

namespace Walk.Services;

public sealed class DefaultBrowserService : IDefaultBrowserService
{
    private const string FallbackBrowserName = "default browser";
    private static readonly string[] UrlSchemes = ["http", "https"];

    public string BrowserDisplayName => ResolveBrowserRegistration().DisplayName;

    public void SearchWeb(string query)
    {
        var trimmedQuery = query.Trim();
        if (trimmedQuery.Length == 0)
            return;

        if (TryNormalizeUrl(trimmedQuery, out var normalizedUrl))
        {
            ProcessHelper.Launch(normalizedUrl, asAdmin: false);
            return;
        }

        var registration = ResolveBrowserRegistration();
        if (!string.IsNullOrWhiteSpace(registration.ExecutablePath))
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = registration.ExecutablePath,
                UseShellExecute = true,
            };

            if (IsFirefox(registration.ExecutablePath))
            {
                startInfo.ArgumentList.Add("-search");
                startInfo.ArgumentList.Add(trimmedQuery);
            }
            else
            {
                // Prefix with '? ' to force an address-bar search for inputs that look URL-like.
                startInfo.ArgumentList.Add($"? {trimmedQuery}");
            }

            Process.Start(startInfo);
            return;
        }

        ProcessHelper.Launch($"https://www.bing.com/search?q={Uri.EscapeDataString(trimmedQuery)}", asAdmin: false);
    }

    private static BrowserRegistration ResolveBrowserRegistration()
    {
        var progId = ReadUserChoiceProgId("https") ?? ReadUserChoiceProgId("http");
        var executablePath = progId is null ? null : TryReadExecutablePath(progId);
        var displayName =
            TryReadApplicationName(progId) ??
            TryReadDescriptionFromExecutable(executablePath) ??
            InferBrowserNameFromExecutable(executablePath) ??
            FallbackBrowserName;

        return new BrowserRegistration(executablePath, displayName);
    }

    private static string? ReadUserChoiceProgId(string scheme)
    {
        using var userChoiceKey = Registry.CurrentUser.OpenSubKey(
            $@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\{scheme}\UserChoice");
        return userChoiceKey?.GetValue("ProgId") as string;
    }

    private static string? TryReadExecutablePath(string progId)
    {
        using var commandKey = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
        var command = commandKey?.GetValue(null) as string;
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var expandedCommand = Environment.ExpandEnvironmentVariables(command.Trim());
        if (expandedCommand.StartsWith('"'))
        {
            var closingQuoteIndex = expandedCommand.IndexOf('"', 1);
            if (closingQuoteIndex > 1)
                return expandedCommand[1..closingQuoteIndex];
        }

        var executableSuffixIndex = expandedCommand.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return executableSuffixIndex > 0
            ? expandedCommand[..(executableSuffixIndex + 4)].Trim()
            : null;
    }

    private static string? TryReadApplicationName(string? progId)
    {
        if (string.IsNullOrWhiteSpace(progId))
            return null;

        foreach (var keyPath in new[]
        {
            $@"{progId}\Application",
            progId,
        })
        {
            using var key = Registry.ClassesRoot.OpenSubKey(keyPath);
            var applicationName = key?.GetValue("ApplicationName") as string;
            if (!string.IsNullOrWhiteSpace(applicationName))
                return applicationName;
        }

        return null;
    }

    private static string? TryReadDescriptionFromExecutable(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            return null;

        try
        {
            var description = FileVersionInfo.GetVersionInfo(executablePath).FileDescription;
            return string.IsNullOrWhiteSpace(description) ? null : description;
        }
        catch
        {
            return null;
        }
    }

    private static string? InferBrowserNameFromExecutable(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return null;

        var executableName = Path.GetFileNameWithoutExtension(executablePath);
        if (string.IsNullOrWhiteSpace(executableName))
            return null;

        return executableName.ToLowerInvariant() switch
        {
            "chrome" => "Google Chrome",
            "msedge" => "Microsoft Edge",
            "firefox" => "Mozilla Firefox",
            "brave" => "Brave",
            "opera" => "Opera",
            _ => executableName,
        };
    }

    private static bool IsFirefox(string executablePath)
    {
        var executableName = Path.GetFileNameWithoutExtension(executablePath);
        return string.Equals(executableName, "firefox", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNormalizeUrl(string value, out string normalizedUrl)
    {
        normalizedUrl = "";
        if (Uri.TryCreate(value, UriKind.Absolute, out var absoluteUri) &&
            UrlSchemes.Contains(absoluteUri.Scheme, StringComparer.OrdinalIgnoreCase))
        {
            normalizedUrl = absoluteUri.AbsoluteUri;
            return true;
        }

        if (!LooksLikeWebHost(value))
            return false;

        if (Uri.TryCreate($"https://{value}", UriKind.Absolute, out var inferredUri))
        {
            normalizedUrl = inferredUri.AbsoluteUri;
            return true;
        }

        return false;
    }

    private static bool LooksLikeWebHost(string value)
    {
        if (value.Contains(' ') || value.Contains('\t'))
            return false;

        if (string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        if (value.Contains(':'))
        {
            var hostPart = value[..value.LastIndexOf(':')];
            if (string.Equals(hostPart, "localhost", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return value.Contains('.') && !Path.IsPathRooted(value) && !value.StartsWith(@".\", StringComparison.Ordinal);
    }

    private sealed record BrowserRegistration(string? ExecutablePath, string DisplayName);
}
