using FluentAssertions;
using Walk.Plugins;
using Walk.Services;

namespace Walk.Tests.Plugins;

public class WebSearchPluginTests
{
    [Fact]
    public async Task QueryAsync_Returns_Search_Result_With_Default_Browser_Name()
    {
        var browserService = new FakeDefaultBrowserService("Google Chrome");
        var plugin = new WebSearchPlugin(browserService);

        var results = await plugin.QueryAsync("docs", CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Title.Should().Be("Search the web in Google Chrome for docs");
        results[0].Subtitle.Should().Be("Uses your browser's default search engine");
        results[0].PluginName.Should().Be("Web");
    }

    [Fact]
    public async Task Search_Action_Uses_Original_Query_Text()
    {
        var browserService = new FakeDefaultBrowserService("Firefox");
        var plugin = new WebSearchPlugin(browserService);
        var results = await plugin.QueryAsync("best editor", CancellationToken.None);

        results[0].Actions.Should().ContainSingle();
        results[0].Actions[0].Execute();

        browserService.SearchedQueries.Should().ContainSingle().Which.Should().Be("best editor");
    }

    private sealed class FakeDefaultBrowserService(string browserDisplayName) : IDefaultBrowserService
    {
        public string BrowserDisplayName { get; } = browserDisplayName;
        public List<string> SearchedQueries { get; } = [];

        public void SearchWeb(string query)
        {
            SearchedQueries.Add(query);
        }
    }
}
