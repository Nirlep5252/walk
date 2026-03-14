using System.IO;
using FluentAssertions;
using Walk.Models;
using Walk.Services;

namespace Walk.Tests.Services;

public class RunHistoryServiceTests : IDisposable
{
    private readonly string _testDir;

    public RunHistoryServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "walk_runhistory_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public void GetEntries_Returns_Empty_When_No_File_Exists()
    {
        var service = new RunHistoryService(_testDir);

        service.GetEntries().Should().BeEmpty();
    }

    [Fact]
    public void RecordLaunch_Persists_And_Merges_By_Command()
    {
        var service = new RunHistoryService(_testDir);
        var target = new RunTarget
        {
            Title = "Services",
            Command = "services.msc",
            Subtitle = "Manage Windows services",
            Kind = "MMC",
            SupportsRunAsAdmin = true,
        };

        service.RecordLaunch("services", target);
        service.RecordLaunch("serv", target);

        var reloaded = new RunHistoryService(_testDir);
        var entries = reloaded.GetEntries();

        entries.Should().ContainSingle();
        entries[0].Title.Should().Be("Services");
        entries[0].Command.Should().Be("services.msc");
        entries[0].LaunchCount.Should().Be(2);
        entries[0].LastQuery.Should().Be("serv");
        entries[0].SupportsRunAsAdmin.Should().BeTrue();
    }
}
