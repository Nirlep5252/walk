using Walk.Helpers;

namespace Walk.Services;

public sealed class QuickLinkLauncher : IQuickLinkLauncher
{
    public void Launch(string target)
    {
        ProcessHelper.Launch(target, asAdmin: false);
    }
}
