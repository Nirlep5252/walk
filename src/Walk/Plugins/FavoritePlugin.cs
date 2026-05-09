using System.IO;
using System.Windows.Threading;
using Walk.Helpers;
using Walk.Models;
using Walk.Services;
using WpfClipboard = System.Windows.Clipboard;

namespace Walk.Plugins;

public sealed class FavoritePlugin : IQueryPlugin
{
    private const int MaxResults = 20;
    private const double TopFavoriteScore = 1.2;
    private const double FavoriteScoreStep = 0.001;
    private readonly FavoriteService _favoriteService;
    private readonly IAppIndexService? _appIndexService;
    private readonly CurrencyConversionService? _currencyConversionService;

    public FavoritePlugin(
        FavoriteService favoriteService,
        IAppIndexService? appIndexService = null,
        CurrencyConversionService? currencyConversionService = null)
    {
        _favoriteService = favoriteService;
        _appIndexService = appIndexService;
        _currencyConversionService = currencyConversionService;
    }

    public string Name => "Favorites";
    public int Priority => 95;

    public async Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
    {
        IReadOnlyList<FavoriteEntry> entries;
        if (string.IsNullOrWhiteSpace(query))
        {
            entries = _favoriteService.Search("", MaxResults);
        }
        else
        {
            if (!TryParseQuery(query, out var searchTerm))
                return [];

            entries = _favoriteService.Search(searchTerm, MaxResults);
        }

        var results = new List<SearchResult>(entries.Count);
        for (var index = 0; index < entries.Count; index++)
            results.Add(await CreateResultAsync(entries[index], index, ct).ConfigureAwait(false));

        return results;
    }

    private async Task<SearchResult> CreateResultAsync(FavoriteEntry entry, int index, CancellationToken ct)
    {
        var appEntry = entry.Kind == FavoriteKind.App ? FindAppEntry(entry) : null;
        var conversion = await GetCurrencyConversionAsync(entry, ct).ConfigureAwait(false);
        var result = new SearchResult
        {
            Title = conversion?.ResultText ?? appEntry?.Name ?? entry.Title,
            Subtitle = $"{FormatKind(entry.Kind)} - {GetSubtitleDetail(entry, appEntry, conversion)}",
            PluginName = Name,
            Score = TopFavoriteScore - (index * FavoriteScoreStep),
            IconGlyph = GetIconGlyph(entry.Kind),
            Actions =
            [
                new SearchAction
                {
                    Label = IsClipboardFavorite(entry.Kind) ? "Copy Favorite" : "Open Favorite",
                    HintLabel = IsClipboardFavorite(entry.Kind) ? "Copy" : "Open",
                    Execute = () => _favoriteService.Launch(entry.Id),
                    KeyGesture = "Enter",
                },
                new SearchAction
                {
                    Label = "Copy Target",
                    HintLabel = "Copy",
                    Execute = () => WpfClipboard.SetText(entry.Target),
                    KeyGesture = "Ctrl+C",
                    ClosesLauncher = false,
                },
                new SearchAction
                {
                    Label = "Unpin Favorite",
                    HintLabel = "Unpin",
                    Execute = () => _favoriteService.Remove(entry.Id),
                    KeyGesture = "Ctrl+P",
                    ClosesLauncher = false,
                    RefreshesResults = true,
                },
                new SearchAction
                {
                    Label = "Move Favorite Up",
                    HintLabel = "Move Up",
                    Execute = () => _favoriteService.Move(entry.Id, -1),
                    KeyGesture = "Ctrl+Up",
                    ClosesLauncher = false,
                    RefreshesResults = true,
                },
                new SearchAction
                {
                    Label = "Move Favorite Down",
                    HintLabel = "Move Down",
                    Execute = () => _favoriteService.Move(entry.Id, 1),
                    KeyGesture = "Ctrl+Down",
                    ClosesLauncher = false,
                    RefreshesResults = true,
                },
            ],
        };

        if (entry.Kind == FavoriteKind.App)
            PopulateAppIcon(result, entry, appEntry);
        else if (entry.Kind == FavoriteKind.ClipboardImage &&
            !string.IsNullOrWhiteSpace(entry.Target) &&
            File.Exists(entry.Target))
        {
            var imagePath = entry.Target;
            result.SetPreviewLoader(loaderCt => IconExtractor.GetThumbnailAsync(imagePath, loaderCt));

            if (IconExtractor.TryGetCachedThumbnail(imagePath, out var cachedImage))
                result.Icon = cachedImage;
            else
                _ = PopulateImageIconAsync(result, imagePath, CancellationToken.None);
        }

        return result;
    }

    private AppEntry? FindAppEntry(FavoriteEntry favorite)
    {
        if (_appIndexService is null)
            return null;

        var favoriteArguments = favorite.Arguments ?? "";
        return _appIndexService.Entries.FirstOrDefault(entry =>
                   entry.ExecutablePath.Equals(favorite.Target, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(entry.Arguments ?? "", favoriteArguments, StringComparison.Ordinal)) ??
               _appIndexService.Entries.FirstOrDefault(entry =>
                   entry.ExecutablePath.Equals(favorite.Target, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<CurrencyConversionResult?> GetCurrencyConversionAsync(FavoriteEntry entry, CancellationToken ct)
    {
        if (entry.Kind != FavoriteKind.Currency || _currencyConversionService is null)
            return null;

        try
        {
            return await _currencyConversionService.ConvertAsync(entry.Target, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static string GetSubtitleDetail(
        FavoriteEntry entry,
        AppEntry? appEntry,
        CurrencyConversionResult? conversion)
    {
        if (conversion is not null)
            return conversion.RateText;

        if (appEntry is not null)
            return GetAppSubtitle(appEntry);

        if (entry.Kind == FavoriteKind.App &&
            entry.Target.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase))
        {
            return "Installed app";
        }

        return entry.Subtitle ?? entry.Target;
    }

    private static string GetAppSubtitle(AppEntry entry)
    {
        if (entry.ExecutablePath.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase))
            return "Installed app";

        return entry.DisplayPath;
    }

    private static void PopulateAppIcon(SearchResult result, FavoriteEntry entry, AppEntry? appEntry)
    {
        var iconPath = appEntry is not null
            ? GetAppIconPath(appEntry)
            : GetFavoriteIconPath(entry);
        if (iconPath is null)
            return;

        var iconIndex = appEntry?.IconIndex ?? entry.IconIndex;
        if (IconExtractor.TryGetCachedIcon(iconPath, iconIndex, out var cachedIcon))
        {
            result.Icon = cachedIcon;
            return;
        }

        _ = PopulateIconAsync(result, iconPath, iconIndex, CancellationToken.None);
    }

    private static string? GetAppIconPath(AppEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.IconPath))
        {
            var expandedIconPath = Environment.ExpandEnvironmentVariables(entry.IconPath);
            if (File.Exists(expandedIconPath) || IconExtractor.IsShellPath(expandedIconPath))
                return expandedIconPath;
        }

        var expandedExecutablePath = Environment.ExpandEnvironmentVariables(entry.ExecutablePath);
        return File.Exists(expandedExecutablePath) || IconExtractor.IsShellPath(expandedExecutablePath)
            ? expandedExecutablePath
            : null;
    }

    private static string? GetFavoriteIconPath(FavoriteEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.IconPath))
        {
            var expandedIconPath = Environment.ExpandEnvironmentVariables(entry.IconPath);
            if (File.Exists(expandedIconPath) || IconExtractor.IsShellPath(expandedIconPath))
                return expandedIconPath;
        }

        var expandedTarget = Environment.ExpandEnvironmentVariables(entry.Target);
        return File.Exists(expandedTarget) || IconExtractor.IsShellPath(expandedTarget)
            ? expandedTarget
            : null;
    }

    private static bool TryParseQuery(string query, out string searchTerm)
    {
        var trimmed = query.Trim();
        searchTerm = "";
        foreach (var prefix in new[] { "fav", "favorite", "favorites" })
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

    private static bool IsClipboardFavorite(FavoriteKind kind)
    {
        return kind is FavoriteKind.ClipboardText
            or FavoriteKind.ClipboardFiles
            or FavoriteKind.ClipboardImage
            or FavoriteKind.Currency;
    }

    private static string FormatKind(FavoriteKind kind)
    {
        return kind switch
        {
            FavoriteKind.ClipboardText => "Clipboard",
            FavoriteKind.ClipboardFiles => "Clipboard",
            FavoriteKind.ClipboardImage => "Clipboard",
            FavoriteKind.QuickLink => "Quicklink",
            FavoriteKind.SystemCommand => "System",
            _ => kind.ToString(),
        };
    }

    private static string GetIconGlyph(FavoriteKind kind)
    {
        return kind switch
        {
            FavoriteKind.App => "\u25B6",
            FavoriteKind.Run => "\u2328",
            FavoriteKind.File => "\uD83D\uDCC4",
            FavoriteKind.QuickLink => "\u2197",
            FavoriteKind.ClipboardText => "\uD83D\uDCCB",
            FavoriteKind.ClipboardFiles => "\uD83D\uDCC4",
            FavoriteKind.ClipboardImage => "\uD83D\uDDBC",
            FavoriteKind.Currency => "$",
            FavoriteKind.SystemCommand => "\u23FB",
            _ => "\u2605",
        };
    }

    private static async Task PopulateIconAsync(
        SearchResult result,
        string iconPath,
        int iconIndex,
        CancellationToken ct)
    {
        try
        {
            var icon = await IconExtractor.GetIconAsync(iconPath, iconIndex, ct).ConfigureAwait(false);
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
