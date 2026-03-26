using System.Threading.Channels;
using Walk.Models;
using Walk.Plugins;

namespace Walk.Services;

public sealed class QueryRouter
{
    private IReadOnlyList<IQueryPlugin> _plugins;

    public QueryRouter(IEnumerable<IQueryPlugin> plugins)
    {
        _plugins = OrderPlugins(plugins);
    }

    public void UpdatePlugins(IEnumerable<IQueryPlugin> plugins)
    {
        _plugins = OrderPlugins(plugins);
    }

    public async Task<IReadOnlyList<SearchResult>> RouteAsync(string query, CancellationToken ct)
    {
        var trimmedQuery = query.Trim();
        if (trimmedQuery.Length == 0)
            return [];

        if (ct.IsCancellationRequested)
            return [];

        var plugins = _plugins;
        var tasks = plugins.Select(p => SafeQueryAsync(p, trimmedQuery, ct)).ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        if (ct.IsCancellationRequested)
            return [];

        return SortResults(results.SelectMany(r => r));
    }

    public async Task RouteIncrementalAsync(
        string query,
        Func<IReadOnlyList<SearchResult>, Task> onResultsAvailable,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onResultsAvailable);

        var trimmedQuery = query.Trim();
        if (trimmedQuery.Length == 0 || ct.IsCancellationRequested)
            return;

        var plugins = _plugins;
        if (plugins.Count == 0)
            return;

        var updates = Channel.CreateUnbounded<PluginResultsUpdate>();
        var resultsByPlugin = new Dictionary<IQueryPlugin, List<SearchResult>>();
        var remainingPlugins = plugins.Count;

        var pluginTasks = plugins
            .Select(plugin => PublishPluginResultsAsync(plugin, trimmedQuery, updates.Writer, ct, () =>
            {
                if (Interlocked.Decrement(ref remainingPlugins) == 0)
                    updates.Writer.TryComplete();
            }))
            .ToArray();

        await foreach (var update in updates.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            if (ct.IsCancellationRequested)
                break;

            if (update.Append)
            {
                if (!resultsByPlugin.TryGetValue(update.Plugin, out var existingResults))
                {
                    existingResults = [];
                    resultsByPlugin[update.Plugin] = existingResults;
                }

                existingResults.AddRange(update.Results);
            }
            else
            {
                resultsByPlugin[update.Plugin] = update.Results.ToList();
            }

            var orderedResults = SortResults(resultsByPlugin.Values.SelectMany(result => result));

            await onResultsAvailable(orderedResults).ConfigureAwait(false);
        }

        await Task.WhenAll(pluginTasks).ConfigureAwait(false);
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

    private static async Task PublishPluginResultsAsync(
        IQueryPlugin plugin,
        string query,
        ChannelWriter<PluginResultsUpdate> writer,
        CancellationToken ct,
        Action onCompleted)
    {
        try
        {
            if (plugin is IIncrementalQueryPlugin incrementalPlugin)
            {
                await Task.Run(
                    () => incrementalPlugin.QueryIncrementalAsync(
                        query,
                        async results =>
                        {
                            if (!ct.IsCancellationRequested)
                                await writer.WriteAsync(new PluginResultsUpdate(plugin, results, Append: true), ct).ConfigureAwait(false);
                        },
                        ct),
                    ct).ConfigureAwait(false);
                return;
            }

            var results = await SafeQueryAsync(plugin, query, ct).ConfigureAwait(false);
            if (!ct.IsCancellationRequested)
                await writer.WriteAsync(new PluginResultsUpdate(plugin, results, Append: false), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            onCompleted();
        }
    }

    private static IReadOnlyList<IQueryPlugin> OrderPlugins(IEnumerable<IQueryPlugin> plugins)
    {
        return plugins.OrderByDescending(p => p.Priority).ToList();
    }

    private static List<SearchResult> SortResults(IEnumerable<SearchResult> results)
    {
        return results
            .OrderBy(GetResultCategoryRank)
            .ThenByDescending(result => result.Score)
            .ToList();
    }

    private static int GetResultCategoryRank(SearchResult result)
    {
        return result.PluginName switch
        {
            "Apps" => 0,
            "Files" => 2,
            _ => 1,
        };
    }

    private sealed record PluginResultsUpdate(IQueryPlugin Plugin, IReadOnlyList<SearchResult> Results, bool Append);
}
