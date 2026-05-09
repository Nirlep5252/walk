using System.IO;
using FluentAssertions;
using Walk.Models;
using Walk.Plugins;
using Walk.Services;

namespace Walk.Tests.Plugins;

public class SystemCommandPluginTests
{
    private readonly SystemCommandPlugin _plugin = new();

    [Theory]
    [InlineData("shutdown")]
    [InlineData("restart")]
    [InlineData("sleep")]
    [InlineData("lock")]
    [InlineData("log off")]
    [InlineData("recycle bin")]
    [InlineData("settings")]
    public async Task QueryAsync_Finds_Known_Commands(string query)
    {
        var results = await _plugin.QueryAsync(query, CancellationToken.None);
        results.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("shut")]
    [InlineData("rest")]
    [InlineData("loc")]
    [InlineData("setings")]
    public async Task QueryAsync_Finds_Commands_By_Partial_Match(string query)
    {
        var results = await _plugin.QueryAsync(query, CancellationToken.None);
        results.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("2+2")]
    [InlineData("xyz123")]
    public async Task QueryAsync_Returns_Empty_For_Non_Commands(string query)
    {
        var results = await _plugin.QueryAsync(query, CancellationToken.None);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task Pin_Action_Adds_System_Command_To_Favorites()
    {
        var testDir = Path.Combine(Path.GetTempPath(), "walk_systemcommandplugin_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        try
        {
            var favoriteService = new FavoriteService(testDir);
            var plugin = new SystemCommandPlugin(favoriteService);

            var results = await plugin.QueryAsync("lock", CancellationToken.None);
            var pinAction = results[0].Actions.Single(action => action.KeyGesture == "Ctrl+P");

            pinAction.Execute();

            favoriteService.GetEntries().Should().ContainSingle(entry =>
                entry.Kind == FavoriteKind.SystemCommand &&
                entry.Target == "Lock");
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }
}
