using FluentAssertions;
using Walk.Plugins;

namespace Walk.Tests.Plugins;

public sealed class EmojiSymbolPluginTests
{
    private readonly EmojiSymbolPlugin _plugin = new();

    [Fact]
    public async Task QueryAsync_Returns_Empty_For_NonEmoji_Query()
    {
        var results = await _plugin.QueryAsync("rocket", CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData("emoji")]
    [InlineData("em")]
    [InlineData(":")]
    public async Task QueryAsync_Returns_Default_Emoji_For_Explicit_Prefix(string query)
    {
        var results = await _plugin.QueryAsync(query, CancellationToken.None);

        results.Should().NotBeEmpty();
        results[0].PluginName.Should().Be("Emoji");
        results.Should().Contain(result => result.Title == "Grinning Face");
        results.Should().OnlyContain(result => result.HasIcon);
    }

    [Fact]
    public async Task QueryAsync_Searches_Emoji_Names_And_Keywords()
    {
        var results = await _plugin.QueryAsync("emoji rocket", CancellationToken.None);

        results.Should().NotBeEmpty();
        results[0].Title.Should().Be("Rocket");
        results[0].HasIcon.Should().BeTrue();
        results[0].Actions.Should().Contain(action => action.KeyGesture == "Enter");
        results[0].Actions.Should().Contain(action => action.KeyGesture == "Ctrl+U");
    }

    [Fact]
    public async Task QueryAsync_Searches_With_Colon_Prefix()
    {
        var results = await _plugin.QueryAsync(":party", CancellationToken.None);

        results.Should().Contain(result => result.Title == "Party Popper");
    }

    [Theory]
    [InlineData("symbol")]
    [InlineData("symbols")]
    [InlineData("sym")]
    public async Task QueryAsync_Returns_Default_Symbols_For_Explicit_Prefix(string query)
    {
        var results = await _plugin.QueryAsync(query, CancellationToken.None);

        results.Should().NotBeEmpty();
        results.Should().Contain(result => result.Title == "Check Mark");
        results.Should().OnlyContain(result => result.HasIcon);
        results.Should().OnlyContain(result =>
            result.Subtitle != null &&
            result.Subtitle.StartsWith("Symbol", StringComparison.Ordinal));
    }

    [Fact]
    public async Task QueryAsync_Searches_Symbol_Names_And_Keywords()
    {
        var results = await _plugin.QueryAsync("sym rupee", CancellationToken.None);

        results.Should().NotBeEmpty();
        results[0].Title.Should().Be("Indian Rupee Sign");
        results[0].HasIcon.Should().BeTrue();
    }
}
