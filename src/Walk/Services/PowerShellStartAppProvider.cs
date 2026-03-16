using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Walk.Services;

public sealed class PowerShellStartAppProvider : IStartAppProvider
{
    private const string GetStartAppsCommand =
        "Get-StartApps | Select-Object Name,AppID | ConvertTo-Json -Compress";

    public async Task<IReadOnlyList<StartAppInfo>> GetAppsAsync(CancellationToken ct = default)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments =
                        $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{GetStartAppsCommand}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                },
            };

            if (!process.Start())
                return [];

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                return [];

            return ParseStartApps(output);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<StartAppInfo> ParseStartApps(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            var apps = new List<StartAppInfo>(document.RootElement.GetArrayLength());
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (TryParseStartApp(element, out var app))
                    apps.Add(app);
            }

            return apps;
        }

        return TryParseStartApp(document.RootElement, out var singleApp)
            ? [singleApp]
            : [];
    }

    private static bool TryParseStartApp(JsonElement element, out StartAppInfo app)
    {
        app = default!;
        if (!element.TryGetProperty("Name", out var nameElement) ||
            !element.TryGetProperty("AppID", out var appIdElement))
        {
            return false;
        }

        var name = nameElement.GetString()?.Trim();
        var appId = appIdElement.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(appId))
            return false;

        app = new StartAppInfo(name, appId);
        return true;
    }
}
