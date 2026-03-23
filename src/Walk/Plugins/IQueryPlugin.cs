using Walk.Models;

namespace Walk.Plugins;

public interface IQueryPlugin
{
    string Name { get; }
    int Priority { get; }
    Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct);
}

public interface IIncrementalQueryPlugin : IQueryPlugin
{
    Task QueryIncrementalAsync(
        string query,
        Func<IReadOnlyList<SearchResult>, Task> onResultsAvailable,
        CancellationToken ct);
}
