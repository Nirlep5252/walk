using System.IO;
using FluentAssertions;
using Walk.Services;

namespace Walk.Tests.Services;

public class ChangelogServiceTests : IDisposable
{
    private readonly string _testDir;

    public ChangelogServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "walk_changelog_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task StageAsync_StoresPendingEntry_WithoutPromotingItToLatest()
    {
        var service = new ChangelogService(_testDir);
        var entry = new ChangelogEntry
        {
            Version = "0.4.0",
            Markdown = "# Added\n- Faster updates",
            RecordedAtUtc = DateTimeOffset.UtcNow,
        };

        await service.StageAsync(entry);

        var latest = await service.GetLatestAsync();
        var pending = await service.GetPendingAsync("0.4.0");

        latest.Should().BeNull();
        pending.Should().NotBeNull();
        pending!.Version.Should().Be("0.4.0");
        pending.Markdown.Should().Contain("Faster updates");
    }

    [Fact]
    public async Task MarkPendingAsSeenAsync_PromotesPendingEntryToLatest()
    {
        var service = new ChangelogService(_testDir);
        await service.StageAsync(new ChangelogEntry
        {
            Version = "0.4.1",
            Markdown = "Bug fixes",
        });

        await service.MarkPendingAsSeenAsync("0.4.1");

        var pending = await service.GetPendingAsync("0.4.1");
        var latest = await service.GetLatestAsync();

        pending.Should().BeNull();
        latest.Should().NotBeNull();
        latest!.Version.Should().Be("0.4.1");
    }
}
