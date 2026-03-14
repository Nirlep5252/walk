using System.IO;
using System.Windows.Threading;
using Walk.Helpers;
using Walk.Models;
using Walk.Services;

namespace Walk.Plugins;

public sealed class AppSearchPlugin : IQueryPlugin
{
    public string Name => "Apps";
    public int Priority => 50;

    private readonly IAppIndexService _indexService;

    public AppSearchPlugin(IAppIndexService indexService)
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

            var match = MatchEntry(query, entry);
            if (!match.IsMatch || match.Score < 0.1)
                continue;

            var usageBoost = Math.Min(0.1, entry.LaunchCount * 0.005);
            matches.Add((entry, match.Score + usageBoost));
        }

        var topMatches = matches
            .OrderByDescending(static match => match.Score)
            .Take(10)
            .ToList();

        var results = new List<SearchResult>(topMatches.Count);
        foreach (var (entry, score) in topMatches)
        {
            var actions = new List<SearchAction>
            {
                new()
                {
                    Label = "Run",
                    HintLabel = "Run",
                    Execute = () =>
                    {
                        ProcessHelper.Launch(entry.ExecutablePath, asAdmin: false, entry.Arguments, entry.WorkingDirectory);
                        _ = _indexService.RecordLaunchAsync(entry);
                    },
                    KeyGesture = "Enter"
                },
                new()
                {
                    Label = "Run as Administrator",
                    HintLabel = "Admin",
                    Execute = () =>
                    {
                        ProcessHelper.Launch(entry.ExecutablePath, asAdmin: true, entry.Arguments, entry.WorkingDirectory);
                        _ = _indexService.RecordLaunchAsync(entry);
                    },
                    KeyGesture = "Ctrl+Enter"
                },
            };

            var revealPath = GetRevealPath(entry);
            if (revealPath is not null)
            {
                actions.Add(new SearchAction
                {
                    Label = "Open File Location",
                    HintLabel = "Reveal",
                    Execute = () => ProcessHelper.OpenFileLocation(revealPath),
                    KeyGesture = "Ctrl+O"
                });
            }

            var result = new SearchResult
            {
                Title = entry.Name,
                Subtitle = entry.DisplayPath,
                PluginName = Name,
                Score = score,
                IconGlyph = "\u25B6",
                Actions = actions,
            };

            var iconPath = GetIconPath(entry);
            if (iconPath is not null && IconExtractor.TryGetCachedIcon(iconPath, entry.IconIndex, out var cachedIcon))
            {
                result.Icon = cachedIcon;
            }
            else if (iconPath is not null)
            {
                _ = PopulateIconAsync(result, iconPath, entry.IconIndex, ct);
            }

            results.Add(result);
        }

        return Task.FromResult<IReadOnlyList<SearchResult>>(results);
    }

    private static FuzzyMatchResult MatchEntry(string query, AppEntry entry)
    {
        var bestMatch = FuzzyMatcher.Match(query, entry.Name);
        foreach (var alias in GetSearchAliases(entry))
        {
            var aliasMatch = FuzzyMatcher.Match(query, alias);
            if (aliasMatch.IsMatch && aliasMatch.Score > bestMatch.Score)
                bestMatch = aliasMatch;
        }

        return bestMatch;
    }

    private static IEnumerable<string> GetSearchAliases(AppEntry entry)
    {
        foreach (var candidate in new[] { entry.ExecutablePath, entry.RevealPath })
        {
            if (TryGetPathAlias(candidate, out var alias))
                yield return alias;
        }
    }

    private static bool TryGetPathAlias(string? path, out string alias)
    {
        alias = "";
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var expandedPath = Environment.ExpandEnvironmentVariables(path);
        if (!Path.IsPathRooted(expandedPath))
            return false;

        alias = Path.GetFileNameWithoutExtension(expandedPath);
        return alias.Length > 0;
    }

    private static string? GetIconPath(AppEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.IconPath))
        {
            var expandedIconPath = Environment.ExpandEnvironmentVariables(entry.IconPath);
            if (File.Exists(expandedIconPath))
                return expandedIconPath;
        }

        var expandedExecutablePath = Environment.ExpandEnvironmentVariables(entry.ExecutablePath);
        return File.Exists(expandedExecutablePath) ? expandedExecutablePath : null;
    }

    private static string? GetRevealPath(AppEntry entry)
    {
        foreach (var candidate in new[] { entry.RevealPath, entry.ExecutablePath })
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var expandedCandidate = Environment.ExpandEnvironmentVariables(candidate);
            if (File.Exists(expandedCandidate) || Directory.Exists(expandedCandidate))
                return expandedCandidate;
        }

        return null;
    }

    private static async Task PopulateIconAsync(
        SearchResult result,
        string iconPath,
        int iconIndex,
        CancellationToken ct)
    {
        try
        {
            var icon = await IconExtractor.GetIconAsync(iconPath, iconIndex, ct).ConfigureAwait(false);
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
