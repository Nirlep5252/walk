using System.IO;
using FluentAssertions;
using Walk.Plugins;

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
}
