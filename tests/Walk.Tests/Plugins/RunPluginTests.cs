using System.IO;
using FluentAssertions;
using Walk.Models;
using Walk.Plugins;
using Walk.Services;

namespace Walk.Tests.Plugins;

public class RunPluginTests : IDisposable
{
    private readonly string _testDir;
    private readonly RunHistoryService _historyService;
    private readonly FakeRunTargetLauncher _launcher;
    private readonly RunPlugin _plugin;

    public RunPluginTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "walk_runplugin_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _historyService = new RunHistoryService(_testDir);
        _launcher = new FakeRunTargetLauncher();
        _plugin = new RunPlugin(_historyService, _launcher);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task QueryAsync_Finds_Catalog_Entries()
    {
        var results = await _plugin.QueryAsync("startup", CancellationToken.None);

        results.Should().NotBeEmpty();
        results.Should().Contain(result => result.Title == "Startup Folder");
    }

    [Theory]
    [InlineData("terminal", "Windows Terminal")]
    [InlineData("firewall", "Windows Firewall")]
    [InlineData("bluetooth", "Bluetooth Devices")]
    public async Task QueryAsync_Finds_New_Default_Run_Entries(string query, string expectedTitle)
    {
        var results = await _plugin.QueryAsync(query, CancellationToken.None);

        results.Should().Contain(result => result.Title == expectedTitle);
    }

    [Fact]
    public async Task QueryAsync_Uses_Explicit_Run_Prefix()
    {
        var results = await _plugin.QueryAsync("> services", CancellationToken.None);

        results.Should().NotBeEmpty();
        results[0].PluginName.Should().Be("Run");
        results.Should().Contain(result => result.Title == "Services");
    }

    [Fact]
    public async Task QueryAsync_Shows_Discovery_Results_For_Blank_Run_Prefix()
    {
        var results = await _plugin.QueryAsync(">", CancellationToken.None);

        results.Should().NotBeEmpty();
        results.Should().Contain(result => result.Title == "Command Prompt");
    }

    [Fact]
    public async Task QueryAsync_Shows_Full_Discovery_Catalog_For_Blank_Run_Prefix()
    {
        var results = await _plugin.QueryAsync(">", CancellationToken.None);

        results.Should().Contain(result => result.Title == "Taskbar Settings");
        results.Count.Should().BeGreaterThan(20);
    }

    [Fact]
    public async Task QueryAsync_Returns_Direct_Result_For_Explicit_Arbitrary_Command()
    {
        var results = await _plugin.QueryAsync(">randomstringhere", CancellationToken.None);

        results.Should().NotBeEmpty();
        results[0].Title.Should().Be("Run randomstringhere");
        results[0].Actions[0].Execute();

        _launcher.Launched.Should().ContainSingle();
        _launcher.Launched[0].command.Should().Be("randomstringhere");
    }

    [Fact]
    public async Task QueryAsync_Returns_Direct_Result_For_Environment_Path()
    {
        var results = await _plugin.QueryAsync("%TEMP%", CancellationToken.None);

        results.Should().NotBeEmpty();
        results[0].PluginName.Should().Be("Run");
        results[0].Subtitle.Should().Contain("Expanded from %TEMP%");
    }

    [Fact]
    public async Task QueryAsync_Normalizes_Mistyped_Shell_ControlPanel_Target()
    {
        var results = await _plugin.QueryAsync("shell:main.cpl", CancellationToken.None);

        results.Should().NotBeEmpty();
        results[0].Title.Should().Be("Run main.cpl");
        results[0].Subtitle.Should().Be("Normalized from shell:main.cpl");

        results[0].Actions[0].Execute();

        _launcher.Launched.Should().ContainSingle();
        _launcher.Launched[0].command.Should().Be("main.cpl");
    }

    [Fact]
    public async Task Execute_Run_Action_Launches_Target_And_Records_History()
    {
        var results = await _plugin.QueryAsync("services", CancellationToken.None);
        var result = results.Single(searchResult => searchResult.Title == "Services");

        result.Actions[0].Execute();

        _launcher.Launched.Should().ContainSingle();
        _launcher.Launched[0].command.Should().Be("services.msc");
        _launcher.Launched[0].asAdmin.Should().BeFalse();

        var historyEntries = _historyService.GetEntries();
        historyEntries.Should().ContainSingle(entry => entry.Command == "services.msc" && entry.LastQuery == "services");
    }

    [Fact]
    public async Task QueryAsync_Prefers_History_For_Previously_Launched_Target()
    {
        _historyService.RecordLaunch("services", new RunTarget
        {
            Title = "Services",
            Command = "services.msc",
            Subtitle = "Manage Windows services",
            Kind = "MMC",
            SupportsRunAsAdmin = true,
        });

        var results = await _plugin.QueryAsync("services", CancellationToken.None);

        results.Should().NotBeEmpty();
        results[0].Title.Should().Be("Services");
        results[0].Subtitle.Should().StartWith("Recent");
    }

    private sealed class FakeRunTargetLauncher : IRunTargetLauncher
    {
        public List<(string command, bool asAdmin)> Launched { get; } = [];
        public List<string> OpenedLocations { get; } = [];

        public void Launch(RunTarget target, bool asAdmin)
        {
            Launched.Add((target.Command, asAdmin));
        }

        public void OpenFileLocation(string path)
        {
            OpenedLocations.Add(path);
        }
    }
}
