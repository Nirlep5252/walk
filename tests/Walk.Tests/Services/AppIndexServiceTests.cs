using System.IO;
using FluentAssertions;
using Walk.Services;

namespace Walk.Tests.Services;

public class AppIndexServiceTests : IDisposable
{
    private readonly string _testDir;

    public AppIndexServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "walk_appindex_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task BuildIndexAsync_Indexes_CustomProtocolUrlShortcuts()
    {
        var shortcutDir = Path.Combine(_testDir, "shortcuts");
        Directory.CreateDirectory(shortcutDir);

        var iconPath = Path.Combine(_testDir, "deadlock.ico");
        await File.WriteAllBytesAsync(iconPath, [0x00]);

        var shortcutPath = Path.Combine(shortcutDir, "Deadlock.url");
        await File.WriteAllTextAsync(
            shortcutPath,
            $$"""
            [InternetShortcut]
            URL=steam://rungameid/1422450
            IconFile={{iconPath}}
            IconIndex=3
            """);

        var service = new AppIndexService(
            _testDir,
            new AppIndexOptions
            {
                ShortcutDirectories = [shortcutDir],
                AppPathRegistryRoots = [],
                PathDirectories = [],
            },
            new StubStartAppProvider([]));

        await service.BuildIndexAsync();

        var entry = service.Entries.Should().ContainSingle().Subject;
        entry.Name.Should().Be("Deadlock");
        entry.ExecutablePath.Should().Be("steam://rungameid/1422450");
        entry.IconPath.Should().Be(iconPath);
        entry.IconIndex.Should().Be(3);
    }

    [Fact]
    public async Task BuildIndexAsync_Indexes_StartApps_Using_Shell_Launch_Targets()
    {
        var service = new AppIndexService(
            _testDir,
            new AppIndexOptions
            {
                ShortcutDirectories = [],
                AppPathRegistryRoots = [],
                PathDirectories = [],
            },
            new StubStartAppProvider(
            [
                new StartAppInfo(
                    "AMD Software",
                    "AdvancedMicroDevicesInc-2.AMDRadeonSoftware_0a9344xs7nr4m!AMDRadeonsoftwareUWP"),
            ]));

        await service.BuildIndexAsync();

        var entry = service.Entries.Should().ContainSingle().Subject;
        entry.Name.Should().Be("AMD Software");
        entry.ExecutablePath.Should().Be(
            @"shell:AppsFolder\AdvancedMicroDevicesInc-2.AMDRadeonSoftware_0a9344xs7nr4m!AMDRadeonsoftwareUWP");
        entry.SourcePriority.Should().Be(400);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("steam://openurl/https://help.steampowered.com")]
    public async Task BuildIndexAsync_Skips_WebBackedUrlShortcuts(string url)
    {
        var shortcutDir = Path.Combine(_testDir, "shortcuts");
        Directory.CreateDirectory(shortcutDir);

        var shortcutPath = Path.Combine(shortcutDir, "Help.url");
        await File.WriteAllTextAsync(
            shortcutPath,
            $$"""
            [InternetShortcut]
            URL={{url}}
            """);

        var service = new AppIndexService(
            _testDir,
            new AppIndexOptions
            {
                ShortcutDirectories = [shortcutDir],
                AppPathRegistryRoots = [],
                PathDirectories = [],
            },
            new StubStartAppProvider([]));

        await service.BuildIndexAsync();

        service.Entries.Should().BeEmpty();
    }

    private sealed class StubStartAppProvider(IReadOnlyList<StartAppInfo> apps) : IStartAppProvider
    {
        public Task<IReadOnlyList<StartAppInfo>> GetAppsAsync(CancellationToken ct = default)
        {
            return Task.FromResult(apps);
        }
    }
}
