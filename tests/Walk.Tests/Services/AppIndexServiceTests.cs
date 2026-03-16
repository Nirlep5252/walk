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

    [Fact]
    public async Task BuildIndexAsync_Ignores_Legacy_Shortcut_And_Path_Sources()
    {
        var shortcutDir = Path.Combine(_testDir, "shortcuts");
        Directory.CreateDirectory(shortcutDir);

        var pathDir = Path.Combine(_testDir, "path");
        Directory.CreateDirectory(pathDir);

        var shortcutPath = Path.Combine(shortcutDir, "NVIDIA App.url");
        await File.WriteAllTextAsync(
            shortcutPath,
            $$"""
            [InternetShortcut]
            URL=steam://rungameid/1422450
            """);

        var executablePath = Path.Combine(pathDir, "nvidia-smi.exe");
        await File.WriteAllTextAsync(executablePath, "stub");

        var service = new AppIndexService(
            _testDir,
            new AppIndexOptions
            {
                ShortcutDirectories = [shortcutDir],
                AppPathRegistryRoots = [],
                PathDirectories = [pathDir],
            },
            new StubStartAppProvider(
            [
                new StartAppInfo(
                    "NVIDIA App",
                    "NVIDIACorp.NVIDIAControlPanel_56jybvy8sckqj!NVIDIAControlPanel"),
            ]));

        await service.BuildIndexAsync();

        var entry = service.Entries.Should().ContainSingle().Subject;
        entry.Name.Should().Be("NVIDIA App");
        entry.ExecutablePath.Should().Be(
            @"shell:AppsFolder\NVIDIACorp.NVIDIAControlPanel_56jybvy8sckqj!NVIDIAControlPanel");
    }

    private sealed class StubStartAppProvider(IReadOnlyList<StartAppInfo> apps) : IStartAppProvider
    {
        public Task<IReadOnlyList<StartAppInfo>> GetAppsAsync(CancellationToken ct = default)
        {
            return Task.FromResult(apps);
        }
    }
}
