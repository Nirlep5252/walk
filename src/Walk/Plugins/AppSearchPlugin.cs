using System.Windows.Threading;
using Walk.Helpers;
using Walk.Models;
using Walk.Services;

namespace Walk.Plugins;

public sealed class AppSearchPlugin : IQueryPlugin
{
    public string Name => "Apps";
    public int Priority => 50;

    private readonly AppIndexService _indexService;

    public AppSearchPlugin(AppIndexService indexService)
    {
        _indexService = indexService;
    }

    public Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);

        var matches = new List<(AppEntry Entry, double Score)>();

        foreach (var entry in _indexService.Entries)
        {
            ct.ThrowIfCancellationRequested();

            var match = FuzzyMatcher.Match(query, entry.Name);
            if (!match.IsMatch || match.Score < 0.1)
                continue;

            var usageBoost = Math.Min(0.1, entry.LaunchCount * 0.005);
            var finalScore = match.Score + usageBoost;

            matches.Add((entry, finalScore));
        }

        var topMatches = matches
            .OrderByDescending(static match => match.Score)
            .Take(10)
            .ToList();

        var results = new List<SearchResult>(topMatches.Count);
        foreach (var (entry, score) in topMatches)
        {
            var result = new SearchResult
            {
                Title = entry.Name,
                Subtitle = entry.ExecutablePath,
                PluginName = Name,
                Score = score,
                IconGlyph = "\u25B6",
                Actions =
                [
                    new SearchAction
                    {
                        Label = "Run",
                        HintLabel = "Run",
                        Execute = () =>
                        {
                            ProcessHelper.Launch(entry.ExecutablePath, asAdmin: false, entry.Arguments, entry.WorkingDirectory);
                            _ = _indexService.RecordLaunchAsync(entry.ExecutablePath);
                        },
                        KeyGesture = "Enter"
                    },
                    new SearchAction
                    {
                        Label = "Run as Administrator",
                        HintLabel = "Admin",
                        Execute = () =>
                        {
                            ProcessHelper.Launch(entry.ExecutablePath, asAdmin: true, entry.Arguments, entry.WorkingDirectory);
                            _ = _indexService.RecordLaunchAsync(entry.ExecutablePath);
                        },
                        KeyGesture = "Ctrl+Enter"
                    },
                    new SearchAction
                    {
                        Label = "Open File Location",
                        HintLabel = "Reveal",
                        Execute = () => ProcessHelper.OpenFileLocation(entry.ExecutablePath),
                        KeyGesture = "Ctrl+O"
                    },
                ]
            };

            if (IconExtractor.TryGetCachedIcon(entry.ExecutablePath, out var cachedIcon))
            {
                result.Icon = cachedIcon;
            }
            else
            {
                _ = PopulateIconAsync(result, entry.ExecutablePath, ct);
            }

            results.Add(result);
        }

        return Task.FromResult<IReadOnlyList<SearchResult>>(results);
    }

    private static async Task PopulateIconAsync(SearchResult result, string executablePath, CancellationToken ct)
    {
        try
        {
            var icon = await IconExtractor.GetIconAsync(executablePath, ct).ConfigureAwait(false);
            if (icon is null || ct.IsCancellationRequested)
                return;

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                result.Icon = icon;
                return;
            }

            await dispatcher.InvokeAsync(
                () =>
                {
                    if (!ct.IsCancellationRequested)
                        result.Icon = icon;
                },
                DispatcherPriority.Background,
                ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }
}
