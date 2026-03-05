using System.IO;
using FluentAssertions;
using Walk.Plugins;
using Walk.Services;

namespace Walk.Tests.Plugins;

public class CurrencyPluginTests : IDisposable
{
    private readonly string _testDir;
    private readonly CacheService _cache;

    public CurrencyPluginTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "walk_currency_test_" + Guid.NewGuid().ToString("N"));
        _cache = new CacheService(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Theory]
    [InlineData("100 USD to EUR")]
    [InlineData("50 eur in gbp")]
    [InlineData("1000 JPY to USD")]
    [InlineData("25.50 CAD to GBP")]
    public void ParseQuery_Should_Match_Valid_Currency_Patterns(string input)
    {
        CurrencyPlugin.TryParseQuery(input, out var amount, out var from, out var to)
            .Should().BeTrue();
        amount.Should().BeGreaterThan(0);
        from.Should().NotBeNullOrEmpty();
        to.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("2+2")]
    [InlineData("notepad")]
    [InlineData("100 USD")]
    [InlineData("USD to EUR")]
    public void ParseQuery_Should_Not_Match_Invalid_Patterns(string input)
    {
        CurrencyPlugin.TryParseQuery(input, out _, out _, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void Priority_Should_Be_High()
    {
        var plugin = new CurrencyPlugin(_cache, TimeSpan.FromHours(6));
        plugin.Priority.Should().BeGreaterThanOrEqualTo(85);
    }
}
