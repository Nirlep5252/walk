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
