using System.Diagnostics;
using System.Runtime.InteropServices;
using Walk.Helpers;
using Walk.Models;

namespace Walk.Plugins;

public sealed class SystemCommandPlugin : IQueryPlugin
{
    public string Name => "System";
    public int Priority => 70;

    private static readonly List<(string Name, string Description, Action Execute, bool NeedsConfirmation)> Commands =
    [
        ("Shutdown", "Shut down the computer", () => Process.Start("shutdown", "/s /t 0"), true),
        ("Restart", "Restart the computer", () => Process.Start("shutdown", "/r /t 0"), true),
        ("Sleep", "Put the computer to sleep", () => SetSuspendState(false, true, true), false),
        ("Lock", "Lock the workstation", () => LockWorkStation(), false),
        ("Log Off", "Sign out of the current session", () => Process.Start("shutdown", "/l"), true),
        ("Empty Recycle Bin", "Empty the Recycle Bin", () => SHEmptyRecycleBin(IntPtr.Zero, null, 0x07), false),
        ("Open Settings", "Open Windows Settings", () => Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true }), false),
    ];

    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    [DllImport("PowrProf.dll", CharSet = CharSet.Auto)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, int dwFlags);

    public Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);

        var results = new List<SearchResult>();

        foreach (var (name, description, execute, needsConfirmation) in Commands)
        {
            var match = FuzzyMatcher.Match(query, name);
            if (!match.IsMatch || match.Score < 0.2)
                continue;

            results.Add(new SearchResult
            {
                Title = name,
                Subtitle = description,
                PluginName = Name,
                Score = match.Score * 0.85,
                IconGlyph = "\u23FB",
                Actions =
                [
                    new SearchAction
                    {
                        Label = needsConfirmation ? "Execute (requires confirmation)" : "Execute",
                        HintLabel = "Run",
                        Execute = execute,
                        KeyGesture = "Enter"
                    }
                ]
            });
        }

        return Task.FromResult<IReadOnlyList<SearchResult>>(results);
    }
}
