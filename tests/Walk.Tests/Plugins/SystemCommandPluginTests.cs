using FluentAssertions;
using Walk.Plugins;

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
}
