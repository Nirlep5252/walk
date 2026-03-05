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

        var results = new List<SearchResult>();

        foreach (var entry in _indexService.Entries)
        {
            ct.ThrowIfCancellationRequested();

            var match = FuzzyMatcher.Match(query, entry.Name);
            if (!match.IsMatch || match.Score < 0.1)
                continue;

            var usageBoost = Math.Min(0.1, entry.LaunchCount * 0.005);
            var finalScore = match.Score + usageBoost;

            results.Add(new SearchResult
            {
                Title = entry.Name,
                Subtitle = entry.ExecutablePath,
                Icon = IconExtractor.GetIcon(entry.ExecutablePath),
                PluginName = Name,
                Score = finalScore,
                Actions =
                [
                    new SearchAction
                    {
                        Label = "Run",
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
                        Execute = () => ProcessHelper.OpenFileLocation(entry.ExecutablePath),
                        KeyGesture = "Ctrl+O"
                    },
                ]
            });
        }

        IReadOnlyList<SearchResult> sorted = results.OrderByDescending(r => r.Score).Take(10).ToList();
        return Task.FromResult(sorted);
    }
}
