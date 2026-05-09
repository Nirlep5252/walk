using System.IO;
using System.Windows.Threading;
using Walk.Helpers;
using Walk.Models;
using Walk.Services;

namespace Walk.Plugins;

public sealed class ClipboardHistoryPlugin : IQueryPlugin
{
    private const int MaxResults = 20;
    private readonly ClipboardHistoryService _clipboardHistoryService;
    private readonly FavoriteService? _favoriteService;

    public ClipboardHistoryPlugin(ClipboardHistoryService clipboardHistoryService, FavoriteService? favoriteService = null)
    {
        _clipboardHistoryService = clipboardHistoryService;
        _favoriteService = favoriteService;
    }

    public string Name => "Clipboard";
    public int Priority => 88;

    public Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
    {
        if (!TryParseQuery(query, out var searchTerm))
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);

        var entries = _clipboardHistoryService.Search(searchTerm, MaxResults);
        var results = entries
            .Select((entry, index) => CreateResult(entry, index, ct))
            .ToList();

        return Task.FromResult<IReadOnlyList<SearchResult>>(results);
    }

    private SearchResult CreateResult(ClipboardHistoryEntry entry, int index, CancellationToken ct)
    {
        var score = Math.Max(0.5, 0.98 - (index * 0.01));
        var pinnedText = entry.IsPinned ? "Pinned - " : "";
        var kindText = entry.Kind switch
        {
            ClipboardHistoryKind.Files => entry.FilePaths.Count == 1 ? "File" : $"{entry.FilePaths.Count} files",
            ClipboardHistoryKind.Image => "Image",
            _ => "Text",
        };
        var actions = new List<SearchAction>
        {
            new()
            {
                Label = "Copy to Clipboard",
                HintLabel = "Copy",
                Execute = () => _clipboardHistoryService.CopyEntryToClipboard(entry.Id),
                KeyGesture = "Enter",
            },
            new()
            {
                Label = "Copy to Clipboard",
                HintLabel = "Copy",
                Execute = () => _clipboardHistoryService.CopyEntryToClipboard(entry.Id),
                KeyGesture = "Ctrl+C",
                ClosesLauncher = false,
            },
        };

        if (_favoriteService is not null && FavoriteService.FromClipboard(entry) is { } favorite)
            actions.Add(FavoriteService.CreateToggleAction(_favoriteService, favorite));

        actions.Add(new SearchAction
        {
            Label = entry.IsPinned ? "Unpin Clipboard Entry" : "Pin Clipboard Entry",
            HintLabel = entry.IsPinned ? "Unpin Clip" : "Pin Clip",
            Execute = () => _clipboardHistoryService.TogglePinned(entry.Id),
            KeyGesture = "Ctrl+K",
            ClosesLauncher = false,
            RefreshesResults = true,
        });

        actions.Add(new SearchAction
        {
            Label = "Delete Entry",
            HintLabel = "Delete",
            Execute = () => _clipboardHistoryService.DeleteEntry(entry.Id),
            KeyGesture = "Ctrl+X",
        });

        var result = new SearchResult
        {
            Title = entry.Title,
            Subtitle = $"{pinnedText}{kindText} - {FormatRelativeTime(entry.LastCopiedUtc)}",
            PluginName = Name,
            Score = score,
            IconGlyph = entry.Kind switch
            {
                ClipboardHistoryKind.Files => "\uD83D\uDCC4",
                ClipboardHistoryKind.Image => "\uD83D\uDDBC",
                _ => "\uD83D\uDCCB",
            },
            Actions = actions,
        };

        if (entry.Kind == ClipboardHistoryKind.Image &&
            !string.IsNullOrWhiteSpace(entry.ImagePath) &&
            File.Exists(entry.ImagePath))
        {
            var imagePath = entry.ImagePath!;
            result.SetPreviewLoader(loaderCt => IconExtractor.GetThumbnailAsync(imagePath, loaderCt));

            if (IconExtractor.TryGetCachedThumbnail(imagePath, out var cachedImage))
                result.Icon = cachedImage;
            else
                _ = PopulateImageIconAsync(result, imagePath, ct);
        }

        return result;
    }

    private static bool TryParseQuery(string query, out string searchTerm)
    {
        var trimmed = query.Trim();
        searchTerm = "";

        foreach (var prefix in new[] { "clip", "clipboard", "cb" })
        {
            if (trimmed.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return true;

            if (trimmed.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
            {
                searchTerm = trimmed[prefix.Length..].Trim();
                return true;
            }
        }

        return false;
    }

    private static string FormatRelativeTime(DateTime timestampUtc)
    {
        var age = DateTime.UtcNow - timestampUtc;
        if (age.TotalSeconds < 60)
            return "just now";

        if (age.TotalMinutes < 60)
            return $"{Math.Max(1, (int)age.TotalMinutes)}m ago";

        if (age.TotalHours < 24)
            return $"{Math.Max(1, (int)age.TotalHours)}h ago";

        if (age.TotalDays < 30)
            return $"{Math.Max(1, (int)age.TotalDays)}d ago";

        return timestampUtc.ToLocalTime().ToString("yyyy-MM-dd");
    }

    private static async Task PopulateImageIconAsync(SearchResult result, string imagePath, CancellationToken ct)
    {
        try
        {
            var icon = await IconExtractor.GetThumbnailAsync(imagePath, ct).ConfigureAwait(false);
            if (icon is null || ct.IsCancellationRequested)
                return;

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                result.Icon = icon;
                return;
            }

            await dispatcher.InvokeAsync(
                () =>
                {
                    if (!ct.IsCancellationRequested)
                        result.Icon = icon;
                },
                DispatcherPriority.Background,
                ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }
}
