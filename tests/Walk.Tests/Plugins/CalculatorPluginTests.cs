using FluentAssertions;
using Walk.Plugins;

namespace Walk.Tests.Plugins;

public class CalculatorPluginTests
{
    private readonly CalculatorPlugin _plugin = new();

    [Theory]
    [InlineData("2+2", "4")]
    [InlineData("10 * 5", "50")]
    [InlineData("100 / 4", "25")]
    [InlineData("(3 + 4) * 2", "14")]
    [InlineData("Sqrt(144)", "12")]
    [InlineData("Sin(0)", "0")]
    public async Task QueryAsync_Evaluates_Valid_Expressions(string input, string expected)
    {
        var results = await _plugin.QueryAsync(input, CancellationToken.None);
        results.Should().HaveCount(1);
        results[0].Title.Should().Be($"= {expected}");
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("notepad")]
    [InlineData("")]
    [InlineData("100 USD to EUR")]
    public async Task QueryAsync_Returns_Empty_For_Non_Math(string input)
    {
        var results = await _plugin.QueryAsync(input, CancellationToken.None);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_Handles_Division_By_Zero()
    {
        var results = await _plugin.QueryAsync("1/0", CancellationToken.None);
        results.Should().NotBeNull();
    }

    [Fact]
    public void Priority_Should_Be_High()
    {
        _plugin.Priority.Should().BeGreaterThanOrEqualTo(80);
    }

    [Fact]
    public async Task QueryAsync_Evaluates_Power_With_Caret()
    {
        var results = await _plugin.QueryAsync("2^10", CancellationToken.None);
        results.Should().HaveCount(1);
        results[0].Title.Should().Be("= 1024");
    }
}
