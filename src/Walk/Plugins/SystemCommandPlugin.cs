using Walk.Helpers;
using Walk.Models;
using Walk.Services;

namespace Walk.Plugins;

public sealed class SystemCommandPlugin : IQueryPlugin
{
    private readonly FavoriteService? _favoriteService;

    public string Name => "System";
    public int Priority => 70;

    public SystemCommandPlugin(FavoriteService? favoriteService = null)
    {
        _favoriteService = favoriteService;
    }

    public Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);

        var results = new List<SearchResult>();

        foreach (var command in SystemCommandCatalog.Commands)
        {
            var match = FuzzyMatcher.Match(query, command.Name);
            if (!match.IsMatch || match.Score < 0.2)
                continue;

            var actions = new List<SearchAction>
            {
                new()
                {
                    Label = command.NeedsConfirmation ? "Execute (requires confirmation)" : "Execute",
                    HintLabel = "Run",
                    Execute = command.Execute,
                    KeyGesture = "Enter"
                }
            };

            if (_favoriteService is not null)
                actions.Add(FavoriteService.CreateToggleAction(_favoriteService, FavoriteService.FromSystemCommand(command)));

            results.Add(new SearchResult
            {
                Title = command.Name,
                Subtitle = command.Description,
                PluginName = Name,
                Score = match.Score * 0.85,
                IconGlyph = "\u23FB",
                Actions = actions,
            });
        }

        return Task.FromResult<IReadOnlyList<SearchResult>>(results);
    }
}
