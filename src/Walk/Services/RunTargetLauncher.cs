using System.Diagnostics;
using Walk.Helpers;
using Walk.Models;

namespace Walk.Services;

public sealed class RunTargetLauncher : IRunTargetLauncher
{
    public void Launch(RunTarget target, bool asAdmin)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = target.Command,
            ErrorDialog = true,
            ErrorDialogParentHandle = IntPtr.Zero,
            UseShellExecute = true,
        };

        if (asAdmin)
            startInfo.Verb = "runas";

        Process.Start(startInfo);
    }

    public void OpenFileLocation(string path)
    {
        ProcessHelper.OpenFileLocation(path);
    }
}
