namespace Walk.Services;

public interface IFileSearchIndex
{
    bool IsAvailable { get; }

    Task<IReadOnlyList<FileSearchIndexEntry>> SearchAsync(string query, int maxResults, CancellationToken ct);
}

public sealed record FileSearchIndexEntry(string Path, bool IsDirectory, double Score = 0.0);
