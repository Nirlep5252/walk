using System.IO;
using FluentAssertions;
using Walk.Plugins;
using Walk.Services;

namespace Walk.Tests.Plugins;

public sealed class QuickLinkPluginTests : IDisposable
{
    private readonly string _testDir;
    private readonly FakeQuickLinkLauncher _launcher = new();
    private readonly QuickLinkService _service;
    private readonly QuickLinkPlugin _plugin;

    public QuickLinkPluginTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "walk_quicklinkplugin_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _service = new QuickLinkService(_testDir, _launcher);
        _plugin = new QuickLinkPlugin(_service);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task QueryAsync_Returns_Empty_For_NonQuicklink_Query()
    {
        var results = await _plugin.QueryAsync("walk", CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ql")]
    [InlineData("quicklink")]
    [InlineData("quicklinks")]
    public async Task QueryAsync_Returns_Default_Quicklinks_For_Explicit_Prefix(string query)
    {
        var results = await _plugin.QueryAsync(query, CancellationToken.None);

        results.Should().NotBeEmpty();
        results.Should().Contain(result => result.Title == "Open GitHub Search");
        results.Should().OnlyContain(result => result.PluginName == "Quicklinks");
    }

    [Fact]
    public async Task QueryAsync_Searches_Explicit_Quicklinks()
    {
        var results = await _plugin.QueryAsync("ql github", CancellationToken.None);

        results.Should().Contain(result => result.Title == "Open GitHub Search");
    }

    [Fact]
    public async Task QueryAsync_Uses_Direct_Alias_With_Parameter()
    {
        var results = await _plugin.QueryAsync("gh walk launcher", CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Title.Should().Be("Open GitHub Search for walk launcher");

        results[0].Actions[0].Execute();

        _launcher.Launched.Should().ContainSingle().Which.Should().Be("https://github.com/search?q=walk%20launcher");
    }

    [Fact]
    public async Task QueryAsync_Adds_Custom_Quicklink()
    {
        var results = await _plugin.QueryAsync("ql add Walk Repo = https://github.com/Nirlep5252/walk", CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Title.Should().Be("Add Quicklink Walk Repo");
        results[0].Actions[0].Execute();

        _service.Search("walk repo").Should().Contain(entry => entry.Target == "https://github.com/Nirlep5252/walk");
    }

    [Fact]
    public async Task QueryAsync_Shows_Add_Help_For_Incomplete_Add_Command()
    {
        var results = await _plugin.QueryAsync("ql add Walk Repo", CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Title.Should().Be("Add Quicklink");
    }

    [Fact]
    public async Task QueryAsync_Removes_Custom_Quicklink()
    {
        var entry = _service.AddOrUpdate("Walk Repo", "https://github.com/Nirlep5252/walk")!;

        var results = await _plugin.QueryAsync("ql remove walk", CancellationToken.None);

        results.Should().Contain(result => result.Title == "Remove Walk Repo");
        results.Single(result => result.Title == "Remove Walk Repo").Actions[0].Execute();

        _service.Search("walk").Should().NotContain(result => result.Id == entry.Id);
    }

    private sealed class FakeQuickLinkLauncher : IQuickLinkLauncher
    {
        public List<string> Launched { get; } = [];

        public void Launch(string target)
        {
            Launched.Add(target);
        }
    }
}
