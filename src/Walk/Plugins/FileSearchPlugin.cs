using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Walk.Helpers;
using Walk.Models;
using Walk.Services;

namespace Walk.Plugins;

public sealed partial class FileSearchPlugin : IIncrementalQueryPlugin
{
    private const int MaxResults = 0;
    private const int MaxCandidateDirectories = 6;
    private const int PublishBatchSize = 16;
    private readonly IFileSearchIndex? _searchIndex;
    private static readonly HashSet<string> PreviewableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".avif",
        ".bmp",
        ".gif",
        ".heic",
        ".jpeg",
        ".jpg",
        ".m4v",
        ".mkv",
        ".mov",
        ".mp4",
        ".pdf",
        ".png",
        ".tif",
        ".tiff",
        ".webm",
        ".webp",
        ".wmv",
    };

    public string Name => "Files";
    public int Priority => 60;

    [GeneratedRegex(@"^[A-Za-z]:\\|^\\\\|^\\[A-Za-z]")]
    private static partial Regex PathPattern();

    public FileSearchPlugin(IFileSearchIndex? searchIndex = null)
    {
        _searchIndex = searchIndex;
    }

    public async Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
    {
        var results = new List<SearchResult>();
        await QueryIncrementalAsync(
            query,
            batch =>
            {
                results.AddRange(batch);
                return Task.CompletedTask;
            },
            ct).ConfigureAwait(false);
        return results;
    }

    public async Task QueryIncrementalAsync(
        string query,
        Func<IReadOnlyList<SearchResult>, Task> onResultsAvailable,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onResultsAvailable);

        var trimmed = query.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return;

        if (_searchIndex is not null && ShouldUseIndexedSearch(trimmed))
        {
            await _searchIndex.SearchIncrementalAsync(
                trimmed,
                MaxResults,
                entries =>
                {
                    var batch = CreateIndexedResults(entries);
                    return batch.Count == 0
                        ? Task.CompletedTask
                        : onResultsAvailable(batch);
                },
                ct).ConfigureAwait(false);
            return;
        }

        var normalizedQuery = NormalizeQuery(trimmed);
        if (!LooksLikePathQuery(trimmed, normalizedQuery))
            return;

        var searchContext = ResolveSearchContext(normalizedQuery);
        if (searchContext is null)
            return;

        var results = new List<SearchResult>();
        var pendingBatch = new List<SearchResult>(PublishBatchSize);
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

                    var result = CreateResult(entry);
                    results.Add(result);
                    pendingBatch.Add(result);
                    if (pendingBatch.Count >= PublishBatchSize)
                    {
                        await onResultsAvailable(pendingBatch.ToList()).ConfigureAwait(false);
                        pendingBatch.Clear();
                    }

                    if (MaxResults > 0 && results.Count >= MaxResults)
                        return;
                }
            }
            catch
            {
                // Skip inaccessible directories and invalid filters.
            }
        }

        if (pendingBatch.Count > 0)
            await onResultsAvailable(pendingBatch.ToList()).ConfigureAwait(false);
    }

    private static IReadOnlyList<SearchResult> CreateIndexedResults(IReadOnlyList<FileSearchIndexEntry> entries)
    {
        var results = new List<SearchResult>(entries.Count);
        for (int index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var result = CreateResult(entry.Path);
            result.Score = entry.Score > 0
                ? entry.Score
                : Math.Max(0.58, 0.84 - (index * 0.01));
            results.Add(result);
        }

        return results;
    }

    private static SearchResult CreateResult(string entry, double score = 0.7)
    {
        var isDirectory = Directory.Exists(entry);
        var result = new SearchResult
        {
            Title = GetDisplayName(entry),
            Subtitle = entry,
            PluginName = "Files",
            Score = score,
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

        if (!isDirectory && ShouldLoadThumbnail(entry))
            result.SetPreviewLoader(loaderCt => IconExtractor.GetThumbnailAsync(entry, loaderCt));

        return result;
    }

    private static bool ShouldLoadThumbnail(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return !string.IsNullOrWhiteSpace(extension) && PreviewableExtensions.Contains(extension);
    }

    private static bool ShouldUseIndexedSearch(string query)
    {
        return query.Contains('*') ||
               query.Contains('?') ||
               query.Contains('.') ||
               query.Contains(Path.DirectorySeparatorChar) ||
               query.Contains(Path.AltDirectorySeparatorChar) ||
               FileSearchQueryHelper.ShouldUseFuzzySearch(query);
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

    private sealed record SearchContext(IReadOnlyList<string> Directories, string Filter);
}
