using Walk.Models;

namespace Walk.Services;

public interface IAppIndexService
{
    IReadOnlyList<AppEntry> Entries { get; }

    Task RecordLaunchAsync(AppEntry entry);
}
