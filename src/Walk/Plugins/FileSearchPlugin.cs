using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Walk.Helpers;
using Walk.Models;

namespace Walk.Plugins;

public sealed partial class FileSearchPlugin : IQueryPlugin
{
    private const int MaxResults = 20;
    private const int MaxCandidateDirectories = 6;

    public string Name => "Files";
    public int Priority => 60;

    [GeneratedRegex(@"^[A-Za-z]:\\|^\\\\|^\\[A-Za-z]")]
    private static partial Regex PathPattern();

    public Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
    {
        var trimmed = query.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);

        var normalizedQuery = NormalizeQuery(trimmed);
        if (!LooksLikePathQuery(trimmed, normalizedQuery))
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);

        var searchContext = ResolveSearchContext(normalizedQuery);
        if (searchContext is null)
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);

        var results = new List<SearchResult>();
        var seenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in searchContext.Directories)
        {
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory, searchContext.Filter))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!seenEntries.Add(entry))
                        continue;

                    results.Add(CreateResult(entry, ct));
                    if (results.Count >= MaxResults)
                        return Task.FromResult<IReadOnlyList<SearchResult>>(results);
                }
            }
            catch
            {
                // Skip inaccessible directories and invalid filters.
            }
        }

        return Task.FromResult<IReadOnlyList<SearchResult>>(results);
    }

    private static SearchResult CreateResult(string entry, CancellationToken ct)
    {
        var isDirectory = Directory.Exists(entry);
        var result = new SearchResult
        {
            Title = GetDisplayName(entry),
            Subtitle = entry,
            PluginName = "Files",
            Score = 0.7,
            IconGlyph = isDirectory ? "\uD83D\uDCC1" : "\uD83D\uDCC4",
            Actions =
            [
                new SearchAction
                {
                    Label = "Open",
                    HintLabel = "Open",
                    Execute = () => Process.Start(new ProcessStartInfo(entry) { UseShellExecute = true }),
                    KeyGesture = "Enter"
                },
                new SearchAction
                {
                    Label = "Open Containing Folder",
                    HintLabel = "Reveal",
                    Execute = () => ProcessHelper.OpenFileLocation(entry),
                    KeyGesture = "Ctrl+O"
                },
                new SearchAction
                {
                    Label = "Copy Path",
                    HintLabel = "Copy",
                    Execute = () => System.Windows.Clipboard.SetText(entry),
                    KeyGesture = "Ctrl+C",
                    ClosesLauncher = false
                }
            ]
        };

        if (!isDirectory)
        {
            if (IconExtractor.TryGetCachedIcon(entry, 0, out var cachedIcon))
            {
                result.Icon = cachedIcon;
            }
            else
            {
                _ = PopulateIconAsync(result, entry, ct);
            }
        }

        return result;
    }

    private static SearchContext? ResolveSearchContext(string normalizedQuery)
    {
        if (Directory.Exists(normalizedQuery))
            return new SearchContext([normalizedQuery], "*");

        var deepestDirectory = FindDeepestExistingDirectory(normalizedQuery);
        if (deepestDirectory is null)
            return null;

        var relativePath = normalizedQuery[deepestDirectory.Length..]
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.IsNullOrWhiteSpace(relativePath))
            return new SearchContext([deepestDirectory], "*");

        var endsWithSeparator =
            normalizedQuery.EndsWith(Path.DirectorySeparatorChar) ||
            normalizedQuery.EndsWith(Path.AltDirectorySeparatorChar);

        var pathSegments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        if (pathSegments.Length == 0)
            return new SearchContext([deepestDirectory], "*");

        var candidateDirectories = new List<string> { deepestDirectory };
        var directorySegments = endsWithSeparator ? pathSegments : pathSegments[..^1];
        foreach (var segment in directorySegments)
        {
            candidateDirectories = FindMatchingDirectories(candidateDirectories, segment);
            if (candidateDirectories.Count == 0)
                return null;
        }

        var filter = endsWithSeparator
            ? "*"
            : BuildFilter(pathSegments[^1]);

        return new SearchContext(candidateDirectories, filter);
    }

    private static List<string> FindMatchingDirectories(IEnumerable<string> parents, string segment)
    {
        var matches = new List<(string Path, double Score)>();

        foreach (var parent in parents)
        {
            try
            {
                var directoryMatches = Directory.EnumerateDirectories(parent)
                    .Select(path => (Path: path, Name: Path.GetFileName(path)))
                    .Select(match => (match.Path, Score: GetSegmentScore(segment, match.Name)))
                    .Where(match => match.Score > 0.0)
                    .OrderByDescending(match => match.Score)
                    .Take(MaxCandidateDirectories);

                matches.AddRange(directoryMatches);
            }
            catch
            {
                // Skip inaccessible directories.
            }
        }

        return matches
            .OrderByDescending(match => match.Score)
            .Select(match => match.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxCandidateDirectories)
            .ToList();
    }

    private static double GetSegmentScore(string segment, string directoryName)
    {
        if (directoryName.StartsWith(segment, StringComparison.OrdinalIgnoreCase))
            return 1.0;

        var fuzzyMatch = FuzzyMatcher.Match(segment, directoryName);
        return fuzzyMatch.IsMatch && fuzzyMatch.Score >= 0.35
            ? fuzzyMatch.Score
            : 0.0;
    }

    private static string BuildFilter(string segment)
    {
        return string.IsNullOrWhiteSpace(segment) ? "*" : $"{segment}*";
    }

    private static string? FindDeepestExistingDirectory(string path)
    {
        var current = path;
        while (!string.IsNullOrWhiteSpace(current) && !Directory.Exists(current))
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.Ordinal))
                return null;

            current = parent;
        }

        return Directory.Exists(current) ? current : null;
    }

    private static bool LooksLikePathQuery(string rawQuery, string normalizedQuery)
    {
        if (rawQuery.StartsWith("~", StringComparison.Ordinal))
            return true;

        if (rawQuery.StartsWith("%", StringComparison.Ordinal) && rawQuery.IndexOf('%', 1) > 1)
            return true;

        return PathPattern().IsMatch(normalizedQuery);
    }

    private static string NormalizeQuery(string query)
    {
        var normalized = query.Trim();
        if (normalized.StartsWith("~", StringComparison.Ordinal))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                var remainder = normalized[1..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                normalized = string.IsNullOrWhiteSpace(remainder)
                    ? userProfile
                    : Path.Combine(userProfile, remainder);
            }
        }

        normalized = Environment.ExpandEnvironmentVariables(normalized);
        return normalized.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    private static string GetDisplayName(string entry)
    {
        var name = Path.GetFileName(entry.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? entry : name;
    }

    private static async Task PopulateIconAsync(SearchResult result, string path, CancellationToken ct)
    {
        try
        {
            var icon = await IconExtractor.GetIconAsync(path, 0, ct).ConfigureAwait(false);
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

    private sealed record SearchContext(IReadOnlyList<string> Directories, string Filter);
}
