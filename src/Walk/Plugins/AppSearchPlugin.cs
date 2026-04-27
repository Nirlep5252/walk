using System.IO;
using System.Windows.Threading;
using Walk.Helpers;
using Walk.Models;
using Walk.Services;

namespace Walk.Plugins;

public sealed class AppSearchPlugin : IQueryPlugin
{
    private const double HabitFrequencyWeight = 0.58;
    private const double HabitRecencyWeight = 0.42;
    private const double HabitRecencyHalfLifeDays = 10.0;

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
            return Task.FromResult(GetHabitResults(ct));

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
            results.Add(CreateResult(entry, score, ct));

        return Task.FromResult<IReadOnlyList<SearchResult>>(results);
    }

    private IReadOnlyList<SearchResult> GetHabitResults(CancellationToken ct)
    {
        var usedEntries = _indexService.Entries
            .Where(static entry => entry.LaunchCount > 0)
            .ToList();

        if (usedEntries.Count == 0)
            return [];

        var maxLaunchCount = usedEntries.Max(static entry => entry.LaunchCount);

        return usedEntries
            .Select(entry => (Entry: entry, Score: GetHabitScore(entry, maxLaunchCount)))
            .OrderByDescending(static match => match.Score)
            .ThenByDescending(static match => match.Entry.LastUsed)
            .ThenBy(static match => match.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .Select(match => CreateResult(match.Entry, match.Score, ct))
            .ToList();
    }

    private SearchResult CreateResult(AppEntry entry, double score, CancellationToken ct)
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
            Subtitle = GetSubtitle(entry),
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

        return result;
    }

    private static double GetHabitScore(AppEntry entry, int maxLaunchCount)
    {
        var frequencyScore = maxLaunchCount <= 1
            ? 1.0
            : Math.Log(entry.LaunchCount + 1) / Math.Log(maxLaunchCount + 1);
        var ageDays = Math.Max(0.0, (DateTime.UtcNow - entry.LastUsed).TotalDays);
        var recencyScore = Math.Pow(0.5, ageDays / HabitRecencyHalfLifeDays);

        return 0.5 + ((frequencyScore * HabitFrequencyWeight) + (recencyScore * HabitRecencyWeight)) * 0.49;
    }

    private static FuzzyMatchResult MatchEntry(string query, AppEntry entry)
    {
        var bestScore = ScoreDisplayName(FuzzyMatcher.Match(query, entry.Name));
        var isMatch = bestScore > 0;

        foreach (var alias in GetSearchAliases(entry).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var aliasMatch = FuzzyMatcher.Match(query, alias);
            if (!aliasMatch.IsMatch)
                continue;

            isMatch = true;
            bestScore = Math.Max(bestScore, ScoreAlias(aliasMatch));
        }

        if (!isMatch)
            return new FuzzyMatchResult(false, 0.0);

        return new FuzzyMatchResult(true, bestScore + GetSourceBoost(entry.SourcePriority));
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

    private static double ScoreDisplayName(FuzzyMatchResult match)
    {
        return match.IsMatch ? match.Score : 0.0;
    }

    private static double ScoreAlias(FuzzyMatchResult match)
    {
        return match.IsMatch ? match.Score * 0.75 : 0.0;
    }

    private static double GetSourceBoost(int sourcePriority)
    {
        return Math.Min(0.15, sourcePriority / 3000d);
    }

    private static string? GetIconPath(AppEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.IconPath))
        {
            var expandedIconPath = Environment.ExpandEnvironmentVariables(entry.IconPath);
            if (File.Exists(expandedIconPath) || IconExtractor.IsShellPath(expandedIconPath))
                return expandedIconPath;
        }

        var expandedExecutablePath = Environment.ExpandEnvironmentVariables(entry.ExecutablePath);
        return File.Exists(expandedExecutablePath) || IconExtractor.IsShellPath(expandedExecutablePath)
            ? expandedExecutablePath
            : null;
    }

    private static string GetSubtitle(AppEntry entry)
    {
        if (entry.ExecutablePath.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase))
            return "Installed app";

        return entry.DisplayPath;
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
