namespace Walk.Services;

public interface IStartAppProvider
{
    Task<IReadOnlyList<StartAppInfo>> GetAppsAsync(CancellationToken ct = default);
}

public sealed record StartAppInfo(string Name, string AppId);
