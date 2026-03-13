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
        var trimmedQuery = query.Trim();
        if (trimmedQuery.Length == 0)
            return [];

        if (ct.IsCancellationRequested)
            return [];

        var tasks = _plugins.Select(p => SafeQueryAsync(p, trimmedQuery, ct)).ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        if (ct.IsCancellationRequested)
            return [];

        return results
            .SelectMany(r => r)
            .OrderByDescending(r => r.Score)
            .ToList();
    }

    private static async Task<IReadOnlyList<SearchResult>> SafeQueryAsync(
        IQueryPlugin plugin, string query, CancellationToken ct)
    {
        try
        {
            return await Task.Run(() => plugin.QueryAsync(query, ct), ct).ConfigureAwait(false);
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
