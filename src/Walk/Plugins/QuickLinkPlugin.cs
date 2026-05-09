using System.Text.RegularExpressions;
using Walk.Models;
using Walk.Services;
using WpfClipboard = System.Windows.Clipboard;

namespace Walk.Plugins;

public sealed partial class QuickLinkPlugin : IQueryPlugin
{
    private const int MaxResults = 20;
    private readonly QuickLinkService _quickLinkService;
    private readonly FavoriteService? _favoriteService;

    public QuickLinkPlugin(QuickLinkService quickLinkService, FavoriteService? favoriteService = null)
    {
        _quickLinkService = quickLinkService;
        _favoriteService = favoriteService;
    }

    public string Name => "Quicklinks";
    public int Priority => 87;

    public Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);

        if (TryParseExplicitQuery(trimmed, out var body))
            return Task.FromResult(RouteExplicitQuery(body));

        var aliasResult = TryCreateAliasResult(trimmed);
        return Task.FromResult<IReadOnlyList<SearchResult>>(aliasResult is null ? [] : [aliasResult]);
    }

    private IReadOnlyList<SearchResult> RouteExplicitQuery(string body)
    {
        if (body.Length == 0)
            return _quickLinkService.Search("", MaxResults).Select(entry => CreateOpenResult(entry, "")).ToList();

        if (TryCreateAddResult(body, out var addResult))
            return [addResult];

        if (TryCreateRemoveResults(body, out var removeResults))
            return removeResults;

        return _quickLinkService.Search(body, MaxResults)
            .Select(entry => CreateOpenResult(entry, ""))
            .ToList();
    }

    private SearchResult? TryCreateAliasResult(string query)
    {
        var firstSpace = query.IndexOf(' ');
        var alias = firstSpace < 0 ? query : query[..firstSpace];
        var parameter = firstSpace < 0 ? "" : query[(firstSpace + 1)..].Trim();
        var entry = _quickLinkService.FindAlias(alias);
        return entry is null ? null : CreateOpenResult(entry, parameter, isAliasMatch: true);
    }

    private bool TryCreateAddResult(string body, out SearchResult result)
    {
        result = null!;
        if (!body.StartsWith("add ", StringComparison.OrdinalIgnoreCase) &&
            !body.StartsWith("new ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var spec = body[4..].Trim();
        var separator = AddSeparatorPattern().Match(spec);
        if (!separator.Success)
        {
            result = new SearchResult
            {
                Title = "Add Quicklink",
                Subtitle = "Use ql add Name = https://example.com/{query}",
                PluginName = Name,
                Score = 0.99,
                IconGlyph = "\u2795",
                Actions =
                [
                    new SearchAction
                    {
                        Label = "Copy Example",
                        HintLabel = "Copy",
                        Execute = () => WpfClipboard.SetText("ql add GitHub Search = https://github.com/search?q={query}"),
                        KeyGesture = "Enter",
                    }
                ],
            };
            return true;
        }

        var name = spec[..separator.Index].Trim();
        var target = spec[(separator.Index + separator.Length)..].Trim();

        if (name.Length == 0 || target.Length == 0)
            return false;

        result = new SearchResult
        {
            Title = $"Add Quicklink {name}",
            Subtitle = target,
            PluginName = Name,
            Score = 0.995,
            IconGlyph = "\u2795",
            Actions =
            [
                new SearchAction
                {
                    Label = "Add Quicklink",
                    HintLabel = "Add",
                    Execute = () => _quickLinkService.AddOrUpdate(name, target),
                    KeyGesture = "Enter",
                },
                new SearchAction
                {
                    Label = "Copy Target",
                    HintLabel = "Copy",
                    Execute = () => WpfClipboard.SetText(target),
                    KeyGesture = "Ctrl+C",
                    ClosesLauncher = false,
                },
            ],
        };
        return true;
    }

    private bool TryCreateRemoveResults(string body, out IReadOnlyList<SearchResult> results)
    {
        results = [];
        var command = "";
        if (body.StartsWith("remove ", StringComparison.OrdinalIgnoreCase))
            command = "remove";
        else if (body.StartsWith("delete ", StringComparison.OrdinalIgnoreCase))
            command = "delete";
        else if (body.StartsWith("rm ", StringComparison.OrdinalIgnoreCase))
            command = "rm";

        if (command.Length == 0)
            return false;

        var searchTerm = body[command.Length..].Trim();
        results = _quickLinkService.Search(searchTerm, MaxResults)
            .Select(CreateRemoveResult)
            .ToList();
        return true;
    }

    private SearchResult CreateOpenResult(QuickLinkEntry entry, string parameter, bool isAliasMatch = false)
    {
        var resolvedTarget = QuickLinkService.ResolveTarget(entry, parameter);
        var title = entry.RequiresQuery && parameter.Length > 0
            ? $"Open {entry.Name} for {parameter}"
            : $"Open {entry.Name}";
        var actions = new List<SearchAction>
        {
            new()
            {
                Label = "Open Quicklink",
                HintLabel = "Open",
                Execute = () => _quickLinkService.Launch(entry.Id, parameter),
                KeyGesture = "Enter",
            },
            new()
            {
                Label = "Copy Target",
                HintLabel = "Copy",
                Execute = () => WpfClipboard.SetText(resolvedTarget),
                KeyGesture = "Ctrl+C",
                ClosesLauncher = false,
            },
        };

        if (_favoriteService is not null)
            actions.Add(FavoriteService.CreateToggleAction(_favoriteService, FavoriteService.FromQuickLink(entry, resolvedTarget)));

        actions.Add(new SearchAction
        {
            Label = "Delete Quicklink",
            HintLabel = "Delete",
            Execute = () => _quickLinkService.Remove(entry.Id),
            KeyGesture = "Ctrl+X",
        });

        return new SearchResult
        {
            Title = title,
            Subtitle = $"{entry.Alias} - {resolvedTarget}",
            PluginName = Name,
            Score = isAliasMatch ? 0.995 : entry.UseCount > 0 ? 0.9 : 0.78,
            IconGlyph = "\u2197",
            Actions = actions,
        };
    }

    private SearchResult CreateRemoveResult(QuickLinkEntry entry)
    {
        return new SearchResult
        {
            Title = $"Remove {entry.Name}",
            Subtitle = $"{entry.Alias} - {entry.Target}",
            PluginName = Name,
            Score = 0.99,
            IconGlyph = "\uD83D\uDDD1",
            Actions =
            [
                new SearchAction
                {
                    Label = "Remove Quicklink",
                    HintLabel = "Remove",
                    Execute = () => _quickLinkService.Remove(entry.Id),
                    KeyGesture = "Enter",
                },
            ],
        };
    }

    private static bool TryParseExplicitQuery(string query, out string body)
    {
        body = "";
        foreach (var prefix in new[] { "ql", "quicklink", "quicklinks" })
        {
            if (query.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return true;

            if (query.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
            {
                body = query[prefix.Length..].Trim();
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"\s*(?:=|->|\|)\s*")]
    private static partial Regex AddSeparatorPattern();
}
