using FluentAssertions;
using Walk.Plugins;

namespace Walk.Tests.Plugins;

public class FileSearchPluginTests
{
    private readonly FileSearchPlugin _plugin = new();

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Windows")]
    public async Task QueryAsync_Returns_Results_For_Valid_Paths(string query)
    {
        var results = await _plugin.QueryAsync(query, CancellationToken.None);
        results.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("notepad")]
    [InlineData("2+2")]
    [InlineData("100 USD to EUR")]
    public async Task QueryAsync_Returns_Empty_For_Non_Paths(string query)
    {
        var results = await _plugin.QueryAsync(query, CancellationToken.None);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_Returns_Empty_For_NonExistent_Path()
    {
        var results = await _plugin.QueryAsync(@"Z:\nonexistent\path", CancellationToken.None);
        results.Should().BeEmpty();
    }
}
