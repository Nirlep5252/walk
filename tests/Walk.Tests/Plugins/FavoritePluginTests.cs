using System.IO;
using System.Text.Json;
using FluentAssertions;
using Walk.Models;
using Walk.Plugins;
using Walk.Services;

namespace Walk.Tests.Plugins;

public sealed class FavoritePluginTests : IDisposable
{
    private readonly string _testDir;
    private readonly FakeRunTargetLauncher _launcher = new();
    private readonly FavoriteService _service;
    private readonly FavoritePlugin _plugin;

    public FavoritePluginTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "walk_favoriteplugin_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _service = new FavoriteService(_testDir, _launcher);
        _plugin = new FavoritePlugin(_service);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task QueryAsync_Returns_Favorites_For_Default_Query()
    {
        _service.AddOrUpdate(CreateRunFavorite("PowerShell", "powershell"));

        var results = await _plugin.QueryAsync("", CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Title.Should().Be("PowerShell");
        results[0].PluginName.Should().Be("Favorites");
    }

    [Fact]
    public async Task QueryAsync_Searches_With_Explicit_Favorite_Prefix()
    {
        _service.AddOrUpdate(CreateRunFavorite("PowerShell", "powershell"));
        _service.AddOrUpdate(CreateRunFavorite("Command Prompt", "cmd"));

        var results = await _plugin.QueryAsync("fav power", CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Title.Should().Be("PowerShell");
    }

    [Fact]
    public async Task Enter_Action_Launches_Favorite()
    {
        _service.AddOrUpdate(CreateRunFavorite("PowerShell", "powershell"));
        var results = await _plugin.QueryAsync("fav power", CancellationToken.None);

        results[0].Actions.Single(action => action.KeyGesture == "Enter").Execute();

        _launcher.Launched.Should().ContainSingle().Which.command.Should().Be("powershell");
        _service.GetEntries().Should().ContainSingle().Which.UseCount.Should().Be(1);
    }

    [Fact]
    public async Task Enter_Action_Does_Not_Override_Manual_Order()
    {
        _service.AddOrUpdate(CreateRunFavorite("First", "first"));
        _service.AddOrUpdate(CreateRunFavorite("Second", "second"));
        var results = await _plugin.QueryAsync("", CancellationToken.None);

        results[1].Actions.Single(action => action.KeyGesture == "Enter").Execute();
        results = await _plugin.QueryAsync("", CancellationToken.None);

        results.Select(result => result.Title).Should().ContainInOrder("First", "Second");

        results[0].Actions.Single(action => action.KeyGesture == "Ctrl+Down").Execute();
        results = await _plugin.QueryAsync("", CancellationToken.None);

        results.Select(result => result.Title).Should().ContainInOrder("Second", "First");
    }

    [Fact]
    public async Task Pin_Action_Removes_Favorite_Without_Closing_Launcher()
    {
        _service.AddOrUpdate(CreateRunFavorite("PowerShell", "powershell"));
        var results = await _plugin.QueryAsync("fav power", CancellationToken.None);

        var unpinAction = results[0].Actions.Single(action => action.KeyGesture == "Ctrl+P");
        unpinAction.ClosesLauncher.Should().BeFalse();
        unpinAction.RefreshesResults.Should().BeTrue();
        unpinAction.Execute();

        _service.GetEntries().Should().BeEmpty();
    }

    [Fact]
    public async Task Move_Actions_Reorder_Favorites_Without_Closing_Launcher()
    {
        _service.AddOrUpdate(CreateRunFavorite("First", "first"));
        _service.AddOrUpdate(CreateRunFavorite("Second", "second"));
        var results = await _plugin.QueryAsync("fav", CancellationToken.None);

        var moveDownAction = results[0].Actions.Single(action => action.KeyGesture == "Ctrl+Down");
        moveDownAction.ClosesLauncher.Should().BeFalse();
        moveDownAction.RefreshesResults.Should().BeTrue();
        moveDownAction.Execute();

        _service.GetEntries().Select(entry => entry.Title).Should().ContainInOrder("Second", "First");
    }

    [Fact]
    public async Task App_Favorite_Uses_App_Index_Display_Metadata()
    {
        _service.AddOrUpdate(new FavoriteEntry
        {
            Key = "app:steam",
            Kind = FavoriteKind.App,
            Title = "Steam",
            Subtitle = @"shell:AppsFolder\Valve.Steam",
            Target = @"shell:AppsFolder\Valve.Steam",
        });
        var plugin = new FavoritePlugin(
            _service,
            new StubAppIndexService(
            [
                new AppEntry
                {
                    Name = "Steam",
                    ExecutablePath = @"shell:AppsFolder\Valve.Steam",
                },
            ]));

        var results = await plugin.QueryAsync("fav steam", CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Subtitle.Should().Be("App - Installed app");
    }

    [Fact]
    public async Task Currency_Favorite_Recalculates_Display_From_Query()
    {
        var cache = new CacheService(_testDir);
        var currencyConversionService = new CurrencyConversionService(cache, TimeSpan.FromHours(6));
        var plugin = new FavoritePlugin(_service, currencyConversionService: currencyConversionService);
        _service.AddOrUpdate(FavoriteService.FromCurrency(100, "USD", "EUR"));
        await WriteCachedRatesAsync("USD", new Dictionary<string, decimal> { ["EUR"] = 0.8m });

        var firstResults = await plugin.QueryAsync("fav usd", CancellationToken.None);

        firstResults.Should().ContainSingle();
        firstResults[0].Title.Should().Be("100 USD = 80.00 EUR");

        await WriteCachedRatesAsync("USD", new Dictionary<string, decimal> { ["EUR"] = 0.9m });
        var secondResults = await plugin.QueryAsync("fav usd", CancellationToken.None);

        secondResults[0].Title.Should().Be("100 USD = 90.00 EUR");
    }

    private static FavoriteEntry CreateRunFavorite(string title, string command)
    {
        return new FavoriteEntry
        {
            Key = $"run:{command}",
            Kind = FavoriteKind.Run,
            Title = title,
            Subtitle = $"Open {title}",
            Target = command,
            RunKind = "Command",
        };
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

    private sealed class StubAppIndexService(IReadOnlyList<AppEntry> entries) : IAppIndexService
    {
        public IReadOnlyList<AppEntry> Entries { get; } = entries;

        public Task RecordLaunchAsync(AppEntry entry)
        {
            return Task.CompletedTask;
        }
    }

    private async Task WriteCachedRatesAsync(string baseCurrency, Dictionary<string, decimal> rates)
    {
        var cacheEntry = new
        {
            Data = new CurrencyConversionService.ExchangeRateData
            {
                BaseCurrency = baseCurrency,
                Rates = rates,
            },
            FetchedAt = DateTime.UtcNow,
        };

        var json = JsonSerializer.Serialize(cacheEntry);
        await File.WriteAllTextAsync(Path.Combine(_testDir, $"currency_{baseCurrency}.json"), json);
    }
}
