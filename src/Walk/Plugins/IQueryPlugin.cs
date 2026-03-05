using Walk.Models;

namespace Walk.Plugins;

public interface IQueryPlugin
{
    string Name { get; }
    int Priority { get; }
    Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct);
}
