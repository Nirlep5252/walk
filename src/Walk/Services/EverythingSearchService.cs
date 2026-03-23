using System.Runtime.InteropServices;
using System.Text;
using Walk.Helpers;

namespace Walk.Services;

public sealed class EverythingSearchService : IFileSearchIndex, IDisposable
{
    private const uint EverythingOk = 0;
    private const uint EverythingRequestFullPathAndFileName = 0x00000004;
    private const uint EverythingSortPathAscending = 3;
    private const int PublishBatchSize = 32;
    private const double MinimumScore = 0.22;
    private readonly SemaphoreSlim _queryGate = new(1, 1);
    private readonly EverythingBundledRuntime _runtime;

    public EverythingSearchService(EverythingBundledRuntime runtime)
    {
        _runtime = runtime;
    }

    public bool IsAvailable => _runtime.IsAvailable;

    public async Task<IReadOnlyList<FileSearchIndexEntry>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        var results = new List<FileSearchIndexEntry>();
        await SearchIncrementalAsync(
            query,
            maxResults,
            batch =>
            {
                results.AddRange(batch);
                return Task.CompletedTask;
            },
            ct).ConfigureAwait(false);
        return results;
    }

    public async Task SearchIncrementalAsync(
        string query,
        int maxResults,
        Func<IReadOnlyList<FileSearchIndexEntry>, Task> onResultsAvailable,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onResultsAvailable);

        if (string.IsNullOrWhiteSpace(query) || !IsAvailable)
            return;

        _runtime.EnsureStarted();

        try
        {
            await _queryGate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            if (!FileSearchQueryHelper.ShouldUseFuzzySearch(query))
            {
                var explicitSearch = FileSearchQueryHelper.HasExplicitSearchSyntax(query);
                await StreamScoredQueryResultsAsync(
                    searchQuery: query,
                    scoreQuery: query,
                    useRegex: false,
                    maxResults,
                    onResultsAvailable,
                    ct,
                    preserveExplicitOrdering: explicitSearch).ConfigureAwait(false);
                return;
            }

            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var publishedCount = await StreamScoredQueryResultsAsync(
                searchQuery: query,
                scoreQuery: query,
                useRegex: false,
                maxResults,
                onResultsAvailable,
                ct,
                seenPaths).ConfigureAwait(false);

            if (maxResults > 0 && publishedCount >= maxResults)
                return;

            var regexPattern = FileSearchQueryHelper.BuildRegexPattern(query);
            if (regexPattern.Length == 0)
                return;

            await StreamScoredQueryResultsAsync(
                searchQuery: regexPattern,
                scoreQuery: query,
                useRegex: true,
                maxResults > 0 ? maxResults - publishedCount : 0,
                onResultsAvailable,
                ct,
                seenPaths).ConfigureAwait(false);
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _queryGate.Release();
        }
    }

    public void Dispose()
    {
        _queryGate.Dispose();
    }

    private static bool IsDirectory(uint index)
    {
        return EverythingNative.IsFolderResult(index) || EverythingNative.IsVolumeResult(index);
    }

    private static IReadOnlyList<FileSearchIndexEntry> ExecuteQuery(
        string query,
        bool useRegex,
        int maxResults,
        CancellationToken ct)
    {
        return ExecuteQuery(query, useRegex, maxResults, offset: 0, ct);
    }

    private static IReadOnlyList<FileSearchIndexEntry> ExecuteQuery(
        string query,
        bool useRegex,
        int maxResults,
        uint offset,
        CancellationToken ct)
    {
        EverythingNative.Reset();
        EverythingNative.SetRequestFlags(EverythingRequestFullPathAndFileName);
        EverythingNative.SetMatchPath(true);
        EverythingNative.SetRegex(useRegex);
        EverythingNative.SetSort(EverythingSortPathAscending);
        if (maxResults > 0)
            EverythingNative.SetMax((uint)maxResults);
        EverythingNative.SetOffset(offset);
        EverythingNative.SetSearch(query);

        var success = EverythingNative.Query(wait: true);
        if (!success || EverythingNative.GetLastError() != EverythingOk)
            return [];

        var count = EverythingNative.GetNumResults();
        var boundedCount = maxResults > 0
            ? Math.Min(count, (uint)maxResults)
            : count;
        var results = new List<FileSearchIndexEntry>((int)Math.Min(boundedCount, (uint)int.MaxValue));
        var buffer = new StringBuilder(32768);

        for (uint i = 0; i < boundedCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            buffer.Clear();
            EverythingNative.GetResultFullPathName(i, buffer, (uint)buffer.Capacity);
            if (buffer.Length == 0)
                continue;

            results.Add(new FileSearchIndexEntry(
                buffer.ToString(),
                IsDirectory(i)));
        }

        return results;
    }

    private static async Task<int> StreamScoredQueryResultsAsync(
        string searchQuery,
        string scoreQuery,
        bool useRegex,
        int maxResults,
        Func<IReadOnlyList<FileSearchIndexEntry>, Task> onResultsAvailable,
        CancellationToken ct,
        HashSet<string>? seenPaths = null,
        bool preserveExplicitOrdering = false)
    {
        var remaining = maxResults;
        uint offset = 0;
        var publishedCount = 0;
        seenPaths ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (!ct.IsCancellationRequested)
        {
            var pageSize = remaining > 0
                ? Math.Min(PublishBatchSize, remaining)
                : PublishBatchSize;
            var batch = ExecuteQuery(searchQuery, useRegex, pageSize, offset, ct);
            if (batch.Count == 0)
                return publishedCount;

            var rankedBatch = RankBatch(
                scoreQuery,
                batch,
                seenPaths,
                preserveExplicitOrdering,
                publishedCount);
            if (rankedBatch.Count > 0)
            {
                await onResultsAvailable(rankedBatch).ConfigureAwait(false);
                publishedCount += rankedBatch.Count;
            }

            if (remaining > 0)
            {
                remaining -= rankedBatch.Count;
                if (remaining <= 0)
                    return publishedCount;
            }

            if (batch.Count < pageSize)
                return publishedCount;

            offset += (uint)batch.Count;
        }

        return publishedCount;
    }

    private static IReadOnlyList<FileSearchIndexEntry> RankBatch(
        string query,
        IReadOnlyList<FileSearchIndexEntry> entries,
        HashSet<string> seenPaths,
        bool preserveExplicitOrdering,
        int publishedCount)
    {
        if (entries.Count == 0)
            return [];

        var ranked = new List<FileSearchIndexEntry>(entries.Count);
        var rankOffset = 0;
        foreach (var entry in entries)
        {
            if (!seenPaths.Add(entry.Path))
                continue;

            double score;
            if (entry.Score > 0.0)
            {
                score = entry.Score;
            }
            else if (preserveExplicitOrdering)
            {
                score = Math.Max(0.2, 0.88 - ((publishedCount + rankOffset) * 0.001));
            }
            else
            {
                score = FileSearchQueryHelper.ScorePath(query, entry.Path);
                if (score < MinimumScore)
                    continue;
            }

            ranked.Add(new FileSearchIndexEntry(entry.Path, entry.IsDirectory, score));
            rankOffset++;
        }

        if (preserveExplicitOrdering)
            return ranked;

        return ranked
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Path.Length)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static class EverythingNative
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
        [DllImport("Everything64.dll", CharSet = CharSet.Unicode, EntryPoint = "Everything_SetSearchW")]
        public static extern uint SetSearch(string search);

        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
        [DllImport("Everything64.dll", EntryPoint = "Everything_SetRequestFlags")]
        public static extern void SetRequestFlags(uint requestFlags);

        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
        [DllImport("Everything64.dll", EntryPoint = "Everything_SetMatchPath")]
        public static extern void SetMatchPath([MarshalAs(UnmanagedType.Bool)] bool enable);

        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
        [DllImport("Everything64.dll", EntryPoint = "Everything_SetRegex")]
        public static extern void SetRegex([MarshalAs(UnmanagedType.Bool)] bool enable);

        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
        [DllImport("Everything64.dll", EntryPoint = "Everything_SetMax")]
        public static extern void SetMax(uint max);

        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
        [DllImport("Everything64.dll", EntryPoint = "Everything_SetOffset")]
        public static extern void SetOffset(uint offset);

        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
        [DllImport("Everything64.dll", EntryPoint = "Everything_SetSort")]
        public static extern void SetSort(uint sortType);

        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
        [DllImport("Everything64.dll", CharSet = CharSet.Unicode, EntryPoint = "Everything_QueryW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Query([MarshalAs(UnmanagedType.Bool)] bool wait);

        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
        [DllImport("Everything64.dll", EntryPoint = "Everything_GetLastError")]
        public static extern uint GetLastError();

        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
        [DllImport("Everything64.dll", EntryPoint = "Everything_GetNumResults")]
        public static extern uint GetNumResults();

        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
        [DllImport("Everything64.dll", EntryPoint = "Everything_IsFolderResult")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsFolderResult(uint index);

        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
        [DllImport("Everything64.dll", EntryPoint = "Everything_IsVolumeResult")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsVolumeResult(uint index);

        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
        [DllImport("Everything64.dll", CharSet = CharSet.Unicode, EntryPoint = "Everything_GetResultFullPathNameW")]
        public static extern void GetResultFullPathName(uint index, StringBuilder path, uint maxCount);

        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
        [DllImport("Everything64.dll", EntryPoint = "Everything_Reset")]
        public static extern void Reset();
    }
}
