using Walk.Models;

namespace Walk.Services;

public interface IRunTargetLauncher
{
    void Launch(RunTarget target, bool asAdmin);
    void OpenFileLocation(string path);
}
