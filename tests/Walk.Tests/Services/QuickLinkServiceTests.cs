using System.IO;
using FluentAssertions;
using Walk.Models;
using Walk.Services;

namespace Walk.Tests.Services;

public sealed class QuickLinkServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly FakeQuickLinkLauncher _launcher = new();

    public QuickLinkServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "walk_quicklinks_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public void GetEntries_Seeds_Default_Quicklinks()
    {
        var service = new QuickLinkService(_testDir, _launcher);

        var entries = service.GetEntries();

        entries.Should().Contain(entry => entry.Alias == "gh" && entry.Name == "GitHub Search");
        entries.Should().Contain(entry => entry.Alias == "yt" && entry.Name == "YouTube Search");
    }

    [Fact]
    public void AddOrUpdate_Persists_Custom_Quicklink()
    {
        var service = new QuickLinkService(_testDir, _launcher);

        var entry = service.AddOrUpdate("Walk Repo", "https://github.com/Nirlep5252/walk");

        entry.Should().NotBeNull();
        entry!.Name.Should().Be("Walk Repo");
        entry.Alias.Should().Be("walkrepo");

        var reloaded = new QuickLinkService(_testDir, _launcher);
        reloaded.Search("walk").Should().Contain(result => result.Target == "https://github.com/Nirlep5252/walk");
    }

    [Fact]
    public void FindAlias_Returns_Alias_Match()
    {
        var service = new QuickLinkService(_testDir, _launcher);

        var entry = service.FindAlias("gh");

        entry.Should().NotBeNull();
        entry!.Name.Should().Be("GitHub Search");
    }

    [Fact]
    public void ResolveTarget_Replaces_Escaped_Query()
    {
        var entry = new QuickLinkEntry
        {
            Target = "https://github.com/search?q={query}",
        };

        QuickLinkService.ResolveTarget(entry, "walk launcher").Should().Be("https://github.com/search?q=walk%20launcher");
    }

    [Fact]
    public void ResolveTarget_Replaces_Raw_Query()
    {
        var entry = new QuickLinkEntry
        {
            Target = "file:///C:/Docs/{queryRaw}",
        };

        QuickLinkService.ResolveTarget(entry, "Walk Notes").Should().Be("file:///C:/Docs/Walk Notes");
    }

    [Fact]
    public void Launch_Resolves_Target_And_Records_Use()
    {
        var service = new QuickLinkService(_testDir, _launcher);
        var entry = service.FindAlias("gh")!;

        service.Launch(entry.Id, "walk launcher");

        _launcher.Launched.Should().ContainSingle().Which.Should().Be("https://github.com/search?q=walk%20launcher");
        service.FindAlias("gh")!.UseCount.Should().Be(1);
    }

    [Fact]
    public void Remove_Deletes_Entry()
    {
        var service = new QuickLinkService(_testDir, _launcher);
        var entry = service.AddOrUpdate("Temp Link", "https://example.com")!;

        service.Remove(entry.Id).Should().BeTrue();

        service.GetEntries().Should().NotContain(result => result.Id == entry.Id);
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
