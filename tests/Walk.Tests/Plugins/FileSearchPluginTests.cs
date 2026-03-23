using System.IO;
using FluentAssertions;
using Walk.Models;
using Walk.Plugins;
using Walk.Services;

namespace Walk.Tests.Plugins;

public class FileSearchPluginTests : IDisposable
{
    private readonly FileSearchPlugin _plugin = new();
    private readonly string _testDir;

    public FileSearchPluginTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "walk_filesearch_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("WALK_FILESEARCH_ROOT", null);

        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Windows")]
    public async Task QueryAsync_Returns_Results_For_Valid_Paths(string query)
    {
        var results = await _plugin.QueryAsync(query, CancellationToken.None);
        results.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("notepad")]
    [InlineData("2+2")]
    [InlineData("100 USD to EUR")]
    public async Task QueryAsync_Returns_Empty_For_Non_Paths(string query)
    {
        var results = await _plugin.QueryAsync(query, CancellationToken.None);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_Returns_Empty_For_NonExistent_Path()
    {
        var results = await _plugin.QueryAsync(@"Z:\nonexistent\path", CancellationToken.None);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_Expands_Environment_Variables()
    {
        var documentsDir = Directory.CreateDirectory(Path.Combine(_testDir, "Documents")).FullName;
        Environment.SetEnvironmentVariable("WALK_FILESEARCH_ROOT", _testDir);

        var results = await _plugin.QueryAsync(@"%WALK_FILESEARCH_ROOT%\Doc", CancellationToken.None);

        results.Should().Contain(result => result.Subtitle == documentsDir);
    }

    [Fact]
    public async Task QueryAsync_Matches_Partial_Intermediate_Directories()
    {
        var documentsDir = Directory.CreateDirectory(Path.Combine(_testDir, "Documents")).FullName;
        var notesPath = Path.Combine(documentsDir, "notes.txt");
        await File.WriteAllTextAsync(notesPath, "hello");

        var query = Path.Combine(_testDir, "Doc", "note");
        var results = await _plugin.QueryAsync(query, CancellationToken.None);

        results.Should().Contain(result => result.Subtitle == notesPath);
    }

    [Fact]
    public async Task QueryAsync_Uses_Search_Index_For_Global_Wildcard_Searches()
    {
        var pdfPath = Path.Combine(_testDir, "guide.pdf");
        await File.WriteAllTextAsync(pdfPath, "hello");
        var plugin = new FileSearchPlugin(new StubFileSearchIndex([new FileSearchIndexEntry(pdfPath, false)]));

        var results = await plugin.QueryAsync("*.pdf", CancellationToken.None);

        results.Should().ContainSingle(result => result.Subtitle == pdfPath);
    }

    [Fact]
    public async Task QueryIncrementalAsync_Publishes_Growing_File_Snapshots_From_Index()
    {
        var firstPath = Path.Combine(_testDir, "guide-1.pdf");
        var secondPath = Path.Combine(_testDir, "guide-2.pdf");
        await File.WriteAllTextAsync(firstPath, "a");
        await File.WriteAllTextAsync(secondPath, "b");

        var plugin = new FileSearchPlugin(new StreamingStubFileSearchIndex(
        [
            [new FileSearchIndexEntry(firstPath, false, 0.9)],
            [new FileSearchIndexEntry(secondPath, false, 0.8)]
        ]));

        var snapshots = new List<IReadOnlyList<SearchResult>>();
        await plugin.QueryIncrementalAsync(
            "*.pdf",
            results =>
            {
                snapshots.Add(results.ToList());
                return Task.CompletedTask;
            },
            CancellationToken.None);

        snapshots.Should().HaveCount(2);
        snapshots[0].Select(result => result.Subtitle).Should().ContainSingle().Which.Should().Be(firstPath);
        snapshots[1].Select(result => result.Subtitle).Should().ContainSingle().Which.Should().Be(secondPath);
    }

    [Fact]
    public async Task QueryAsync_Aggregates_All_Indexed_File_Batches()
    {
        var entries = Enumerable.Range(1, 120)
            .Select(index => new FileSearchIndexEntry(Path.Combine(_testDir, $"file-{index}.txt"), false, 1.0 - (index * 0.001)))
            .ToList();

        var plugin = new FileSearchPlugin(new StreamingStubFileSearchIndex(
        [
            entries.Take(60).ToList(),
            entries.Skip(60).ToList()
        ]));

        var results = await plugin.QueryAsync("filesearch", CancellationToken.None);

        results.Should().HaveCount(120);
    }

    [Fact]
    public async Task QueryAsync_Does_Not_Use_Index_For_Short_Plain_Text_Query()
    {
        var index = new TrackingFileSearchIndex();
        var plugin = new FileSearchPlugin(index);

        var results = await plugin.QueryAsync("Stea", CancellationToken.None);

        results.Should().BeEmpty();
        index.SearchIncrementalCallCount.Should().Be(0);
    }

    private sealed class StubFileSearchIndex : IFileSearchIndex
    {
        private readonly IReadOnlyList<FileSearchIndexEntry> _entries;

        public StubFileSearchIndex(IReadOnlyList<FileSearchIndexEntry> entries)
        {
            _entries = entries;
        }

        public bool IsAvailable => true;

        public Task<IReadOnlyList<FileSearchIndexEntry>> SearchAsync(string query, int maxResults, CancellationToken ct)
        {
            var results = maxResults > 0
                ? _entries.Take(maxResults).ToList()
                : _entries.ToList();
            return Task.FromResult<IReadOnlyList<FileSearchIndexEntry>>(results);
        }

        public Task SearchIncrementalAsync(
            string query,
            int maxResults,
            Func<IReadOnlyList<FileSearchIndexEntry>, Task> onResultsAvailable,
            CancellationToken ct)
        {
            return onResultsAvailable(maxResults > 0 ? _entries.Take(maxResults).ToList() : _entries.ToList());
        }
    }

    private sealed class StreamingStubFileSearchIndex : IFileSearchIndex
    {
        private readonly IReadOnlyList<IReadOnlyList<FileSearchIndexEntry>> _snapshots;

        public StreamingStubFileSearchIndex(IReadOnlyList<IReadOnlyList<FileSearchIndexEntry>> snapshots)
        {
            _snapshots = snapshots;
        }

        public bool IsAvailable => true;

        public Task<IReadOnlyList<FileSearchIndexEntry>> SearchAsync(string query, int maxResults, CancellationToken ct)
        {
            var finalSnapshot = _snapshots.LastOrDefault() ?? [];
            var results = maxResults > 0
                ? finalSnapshot.Take(maxResults).ToList()
                : finalSnapshot.ToList();
            return Task.FromResult<IReadOnlyList<FileSearchIndexEntry>>(results);
        }

        public async Task SearchIncrementalAsync(
            string query,
            int maxResults,
            Func<IReadOnlyList<FileSearchIndexEntry>, Task> onResultsAvailable,
            CancellationToken ct)
        {
            foreach (var snapshot in _snapshots)
            {
                ct.ThrowIfCancellationRequested();
                var results = maxResults > 0
                    ? snapshot.Take(maxResults).ToList()
                    : snapshot.ToList();
                await onResultsAvailable(results);
            }
        }
    }

    private sealed class TrackingFileSearchIndex : IFileSearchIndex
    {
        public bool IsAvailable => true;
        public int SearchIncrementalCallCount { get; private set; }

        public Task<IReadOnlyList<FileSearchIndexEntry>> SearchAsync(string query, int maxResults, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<FileSearchIndexEntry>>([]);
        }

        public Task SearchIncrementalAsync(
            string query,
            int maxResults,
            Func<IReadOnlyList<FileSearchIndexEntry>, Task> onResultsAvailable,
            CancellationToken ct)
        {
            SearchIncrementalCallCount++;
            return Task.CompletedTask;
        }
    }
}
