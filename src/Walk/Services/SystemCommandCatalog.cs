using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Walk.Services;

public sealed record SystemCommandEntry(
    string Name,
    string Description,
    Action Execute,
    bool NeedsConfirmation);

public static class SystemCommandCatalog
{
    public static IReadOnlyList<SystemCommandEntry> Commands { get; } =
    [
        new("Shutdown", "Shut down the computer", () => Process.Start("shutdown", "/s /t 0"), true),
        new("Restart", "Restart the computer", () => Process.Start("shutdown", "/r /t 0"), true),
        new("Sleep", "Put the computer to sleep", () => SetSuspendState(false, true, true), false),
        new("Lock", "Lock the workstation", () => LockWorkStation(), false),
        new("Log Off", "Sign out of the current session", () => Process.Start("shutdown", "/l"), true),
        new("Empty Recycle Bin", "Empty the Recycle Bin", () => SHEmptyRecycleBin(IntPtr.Zero, null, 0x07), false),
        new("Open Settings", "Open Windows Settings", () => Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true }), false),
    ];

    public static bool TryGet(string name, out SystemCommandEntry command)
    {
        command = Commands.FirstOrDefault(candidate =>
            candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase))!;
        return command is not null;
    }

    public static bool TryExecute(string name)
    {
        if (!TryGet(name, out var command))
            return false;

        command.Execute();
        return true;
    }

    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    [DllImport("PowrProf.dll", CharSet = CharSet.Auto)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, int dwFlags);
}
