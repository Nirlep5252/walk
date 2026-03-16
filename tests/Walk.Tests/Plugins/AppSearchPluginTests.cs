using FluentAssertions;
using Walk.Models;
using Walk.Plugins;
using Walk.Services;

namespace Walk.Tests.Plugins;

public class AppSearchPluginTests
{
    [Fact]
    public async Task QueryAsync_Matches_ExecutableAlias_WhenDisplayNameDoesNotContainQuery()
    {
        var plugin = new AppSearchPlugin(
            new StubAppIndexService(
            [
                new AppEntry
                {
                    Name = "OBS Studio (64bit)",
                    ExecutablePath = @"C:\Apps\OBS\obs64.exe",
                    RevealPath = @"C:\Apps\OBS\obs64.exe",
                },
            ]));

        var results = await plugin.QueryAsync("obs64", CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Title.Should().Be("OBS Studio (64bit)");
        results[0].Subtitle.Should().Be(@"C:\Apps\OBS\obs64.exe");
    }

    [Fact]
    public async Task QueryAsync_Prefers_Installed_App_Name_Over_Path_Executable_Matches()
    {
        var plugin = new AppSearchPlugin(
            new StubAppIndexService(
            [
                new AppEntry
                {
                    Name = "amd-smi",
                    ExecutablePath = @"C:\Windows\System32\amd-smi.exe",
                    RevealPath = @"C:\Windows\System32\amd-smi.exe",
                    SourcePriority = 100,
                },
                new AppEntry
                {
                    Name = "AMD Software",
                    ExecutablePath = @"shell:AppsFolder\AdvancedMicroDevicesInc-2.AMDRadeonSoftware_0a9344xs7nr4m!AMDRadeonsoftwareUWP",
                    SourcePriority = 400,
                },
            ]));

        var results = await plugin.QueryAsync("AMD", CancellationToken.None);

        results.Should().NotBeEmpty();
        results[0].Title.Should().Be("AMD Software");
    }

    [Fact]
    public async Task QueryAsync_Omits_Reveal_Action_For_ProtocolTargets()
    {
        var plugin = new AppSearchPlugin(
            new StubAppIndexService(
            [
                new AppEntry
                {
                    Name = "Deadlock",
                    ExecutablePath = "steam://rungameid/1422450",
                    IconPath = @"C:\Icons\deadlock.ico",
                },
            ]));

        var results = await plugin.QueryAsync("deadlock", CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Actions.Should().NotContain(action => action.Label == "Open File Location");
    }

    private sealed class StubAppIndexService(IReadOnlyList<AppEntry> entries) : IAppIndexService
    {
        public IReadOnlyList<AppEntry> Entries { get; } = entries;

        public Task RecordLaunchAsync(AppEntry entry)
        {
            return Task.CompletedTask;
        }
    }
}
