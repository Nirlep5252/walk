using Walk.Models;
using Walk.Plugins;

namespace Walk.Services;

public sealed class QueryRouter
{
    private readonly IReadOnlyList<IQueryPlugin> _plugins;

    public QueryRouter(IEnumerable<IQueryPlugin> plugins)
    {
        _plugins = plugins.OrderByDescending(p => p.Priority).ToList();
    }

    public async Task<IReadOnlyList<SearchResult>> RouteAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var tasks = _plugins.Select(p => SafeQuery(p, query, ct));
        var results = await Task.WhenAll(tasks);

        return results
            .SelectMany(r => r)
            .OrderByDescending(r => r.Score)
            .ToList();
    }

    private static async Task<IReadOnlyList<SearchResult>> SafeQuery(
        IQueryPlugin plugin, string query, CancellationToken ct)
    {
        try
        {
            return await plugin.QueryAsync(query, ct);
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        catch
        {
            return [];
        }
    }
}
