using System.Runtime.InteropServices;
using System.Text;
using Walk.Helpers;

namespace Walk.Services;

public sealed class EverythingSearchService : IFileSearchIndex, IDisposable
{
    private const uint EverythingOk = 0;
    private const uint EverythingRequestFullPathAndFileName = 0x00000004;
    private const uint EverythingSortPathAscending = 3;
    private readonly SemaphoreSlim _queryGate = new(1, 1);
    private readonly EverythingBundledRuntime _runtime;

    public EverythingSearchService(EverythingBundledRuntime runtime)
    {
        _runtime = runtime;
    }

    public bool IsAvailable => _runtime.IsAvailable;

    public async Task<IReadOnlyList<FileSearchIndexEntry>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query) || !IsAvailable)
            return [];

        _runtime.EnsureStarted();

        try
        {
            await _queryGate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return [];
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            if (!FileSearchQueryHelper.ShouldUseFuzzySearch(query))
                return ExecuteQuery(query, useRegex: false, maxResults, ct);

            var directMatches = ExecuteQuery(query, useRegex: false, maxResults, ct);
            var regexPattern = FileSearchQueryHelper.BuildRegexPattern(query);
            var fuzzyMatches = regexPattern.Length > 0
                ? ExecuteQuery(regexPattern, useRegex: true, maxResults: 0, ct)
                : [];

            var combined = directMatches
                .Concat(fuzzyMatches)
                .GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var first = group.First();
                    return new FileSearchIndexEntry(
                        first.Path,
                        first.IsDirectory,
                        FileSearchQueryHelper.ScorePath(query, first.Path));
                })
                .Where(entry => entry.Score >= 0.22)
                .OrderByDescending(entry => entry.Score)
                .ThenBy(entry => entry.Path.Length)
                .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase);

            return maxResults > 0
                ? combined.Take(maxResults).ToList()
                : combined.ToList();
        }
        catch (DllNotFoundException)
        {
            return [];
        }
        catch (EntryPointNotFoundException)
        {
            return [];
        }
        catch (OperationCanceledException)
        {
            return [];
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
        EverythingNative.Reset();
        EverythingNative.SetRequestFlags(EverythingRequestFullPathAndFileName);
        EverythingNative.SetMatchPath(true);
        EverythingNative.SetRegex(useRegex);
        EverythingNative.SetSort(EverythingSortPathAscending);
        if (maxResults > 0)
            EverythingNative.SetMax((uint)maxResults);
        EverythingNative.SetOffset(0);
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
