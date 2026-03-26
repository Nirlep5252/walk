using Walk.Models;
using Walk.Services;

namespace Walk.Plugins;

public sealed class WebSearchPlugin : IQueryPlugin
{
    private readonly IDefaultBrowserService _defaultBrowserService;

    public string Name => "Web";
    public int Priority => 1;

    public WebSearchPlugin(IDefaultBrowserService defaultBrowserService)
    {
        _defaultBrowserService = defaultBrowserService;
    }

    public Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
    {
        var trimmedQuery = query.Trim();
        if (trimmedQuery.Length == 0)
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);

        var browserName = _defaultBrowserService.BrowserDisplayName;
        IReadOnlyList<SearchResult> results =
        [
            new SearchResult
            {
                Title = $"Search the web in {browserName} for {trimmedQuery}",
                Subtitle = "Uses your browser's default search engine",
                PluginName = Name,
                Score = 0,
                IconGlyph = "\uD83C\uDF10",
                Actions =
                [
                    new SearchAction
                    {
                        Label = "Search the Web",
                        HintLabel = "Search",
                        Execute = () => _defaultBrowserService.SearchWeb(trimmedQuery),
                        KeyGesture = "Enter",
                    }
                ],
            }
        ];

        return Task.FromResult(results);
    }
}
