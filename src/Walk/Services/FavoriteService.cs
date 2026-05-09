using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Walk.Helpers;
using Walk.Models;
using WpfClipboard = System.Windows.Clipboard;
using WpfTextDataFormat = System.Windows.TextDataFormat;

namespace Walk.Services;

public sealed class FavoriteService
{
    private readonly object _gate = new();
    private readonly string _favoritesPath;
    private readonly string _favoriteImageDirectory;
    private readonly IRunTargetLauncher _runTargetLauncher;
    private readonly CurrencyConversionService? _currencyConversionService;
    private List<FavoriteEntry>? _entries;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public FavoriteService(
        string dataDir,
        IRunTargetLauncher? runTargetLauncher = null,
        CurrencyConversionService? currencyConversionService = null)
    {
        Directory.CreateDirectory(dataDir);
        _favoritesPath = Path.Combine(dataDir, "favorites.json");
        _favoriteImageDirectory = Path.Combine(dataDir, "FavoriteImages");
        _runTargetLauncher = runTargetLauncher ?? new RunTargetLauncher();
        _currencyConversionService = currencyConversionService;
    }

    public IReadOnlyList<FavoriteEntry> GetEntries()
    {
        EnsureLoaded();

        lock (_gate)
        {
            return _entries!
                .OrderBy(static entry => entry.SortOrder)
                .Select(CloneEntry)
                .ToList();
        }
    }

    public IReadOnlyList<FavoriteEntry> Search(string query, int maxResults = 20)
    {
        EnsureLoaded();

        var trimmed = query.Trim();
        var limit = Math.Max(1, maxResults);

        lock (_gate)
        {
            if (trimmed.Length == 0)
            {
                return _entries!
                    .OrderBy(static entry => entry.SortOrder)
                    .Take(limit)
                    .Select(CloneEntry)
                    .ToList();
            }

            return _entries!
                .Select(entry => (Entry: entry, Score: GetMatchScore(trimmed, entry)))
                .Where(static match => match.Score > 0)
                .OrderByDescending(static match => match.Score)
                .ThenBy(static match => match.Entry.SortOrder)
                .ThenBy(static match => match.Entry.Title, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .Select(match => CloneEntry(match.Entry))
                .ToList();
        }
    }

    public bool IsFavorite(string key)
    {
        EnsureLoaded();

        lock (_gate)
        {
            return _entries!.Any(entry => entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        }
    }

    public FavoriteEntry AddOrUpdate(FavoriteEntry favorite)
    {
        EnsureLoaded();
        var storedFavorite = PrepareFavoriteForStorage(favorite);

        lock (_gate)
        {
            var entries = _entries!;
            var existing = entries.FirstOrDefault(entry =>
                entry.Key.Equals(storedFavorite.Key, StringComparison.OrdinalIgnoreCase));
            string? oldImageTarget = null;

            if (existing is null)
            {
                existing = CloneEntry(storedFavorite);
                existing.Id = Guid.NewGuid().ToString("N");
                existing.CreatedUtc = DateTime.UtcNow;
                existing.SortOrder = entries.Count == 0
                    ? 1
                    : entries.Max(static entry => entry.SortOrder) + 1;
                entries.Add(existing);
            }
            else
            {
                if (existing.Kind == FavoriteKind.ClipboardImage &&
                    !existing.Target.Equals(storedFavorite.Target, StringComparison.OrdinalIgnoreCase))
                {
                    oldImageTarget = existing.Target;
                }

                existing.Kind = storedFavorite.Kind;
                existing.Title = storedFavorite.Title;
                existing.Subtitle = storedFavorite.Subtitle;
                existing.Target = storedFavorite.Target;
                existing.Arguments = storedFavorite.Arguments;
                existing.WorkingDirectory = storedFavorite.WorkingDirectory;
                existing.RunKind = storedFavorite.RunKind;
                existing.IconPath = storedFavorite.IconPath;
                existing.IconIndex = storedFavorite.IconIndex;
                existing.SupportsRunAsAdmin = storedFavorite.SupportsRunAsAdmin;
                if (existing.SortOrder <= 0)
                    existing.SortOrder = entries.Max(static entry => entry.SortOrder) + 1;
            }

            SaveUnsafe();
            DeleteUnusedFavoriteImageUnsafe(oldImageTarget);
            return CloneEntry(existing);
        }
    }

    public bool Remove(string key)
    {
        EnsureLoaded();

        lock (_gate)
        {
            var entries = _entries!;
            var removedEntries = entries
                .Where(entry =>
                    entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                    entry.Id.Equals(key, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (removedEntries.Count == 0)
                return false;

            foreach (var entry in removedEntries)
                entries.Remove(entry);

            SaveUnsafe();

            foreach (var entry in removedEntries)
            {
                if (entry.Kind == FavoriteKind.ClipboardImage)
                    DeleteUnusedFavoriteImageUnsafe(entry.Target);
            }

            return true;
        }
    }

    public bool Move(string key, int offset)
    {
        if (offset == 0)
            return false;

        EnsureLoaded();

        lock (_gate)
        {
            var entries = _entries!;
            EnsureSortOrderUnsafe();

            var orderedEntries = entries
                .OrderBy(static entry => entry.SortOrder)
                .ToList();
            var currentIndex = orderedEntries.FindIndex(entry =>
                entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                entry.Id.Equals(key, StringComparison.OrdinalIgnoreCase));

            if (currentIndex < 0)
                return false;

            var targetIndex = Math.Clamp(currentIndex + offset, 0, orderedEntries.Count - 1);
            if (targetIndex == currentIndex)
                return false;

            var currentEntry = orderedEntries[currentIndex];
            orderedEntries.RemoveAt(currentIndex);
            orderedEntries.Insert(targetIndex, currentEntry);

            for (var index = 0; index < orderedEntries.Count; index++)
                orderedEntries[index].SortOrder = index + 1;

            SaveUnsafe();
            return true;
        }
    }

    public void Launch(string entryId)
    {
        var entry = GetEntry(entryId);
        if (entry is null)
            return;

        LaunchEntry(entry);
        RecordUse(entry.Id);
    }

    public static FavoriteEntry FromApp(AppEntry entry)
    {
        return new FavoriteEntry
        {
            Key = BuildKey(FavoriteKind.App, entry.ExecutablePath, entry.Arguments ?? ""),
            Kind = FavoriteKind.App,
            Title = entry.Name,
            Subtitle = GetAppSubtitle(entry),
            Target = entry.ExecutablePath,
            Arguments = entry.Arguments,
            WorkingDirectory = entry.WorkingDirectory,
            IconPath = entry.IconPath,
            IconIndex = entry.IconIndex,
        };
    }

    public static FavoriteEntry FromRunTarget(RunTarget target)
    {
        return new FavoriteEntry
        {
            Key = BuildKey(FavoriteKind.Run, target.Kind, target.Command),
            Kind = FavoriteKind.Run,
            Title = target.Title,
            Subtitle = target.Subtitle ?? target.Command,
            Target = target.Command,
            WorkingDirectory = target.WorkingDirectory,
            RunKind = target.Kind,
            SupportsRunAsAdmin = target.SupportsRunAsAdmin,
        };
    }

    public static FavoriteEntry FromFile(string path)
    {
        var title = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(title))
            title = path;

        return new FavoriteEntry
        {
            Key = BuildKey(FavoriteKind.File, path),
            Kind = FavoriteKind.File,
            Title = title,
            Subtitle = path,
            Target = path,
        };
    }

    public static FavoriteEntry FromQuickLink(QuickLinkEntry entry, string resolvedTarget)
    {
        return new FavoriteEntry
        {
            Key = BuildKey(FavoriteKind.QuickLink, resolvedTarget),
            Kind = FavoriteKind.QuickLink,
            Title = entry.Name,
            Subtitle = $"{entry.Alias} - {resolvedTarget}",
            Target = resolvedTarget,
        };
    }

    public static FavoriteEntry? FromClipboard(ClipboardHistoryEntry entry)
    {
        return entry.Kind switch
        {
            ClipboardHistoryKind.Text when !string.IsNullOrWhiteSpace(entry.Text) => new FavoriteEntry
            {
                Key = BuildKey(FavoriteKind.ClipboardText, HashValue(entry.Text)),
                Kind = FavoriteKind.ClipboardText,
                Title = entry.Title,
                Subtitle = "Clipboard text",
                Target = entry.Text!,
            },
            ClipboardHistoryKind.Files when entry.FilePaths.Count > 0 => new FavoriteEntry
            {
                Key = BuildKey(FavoriteKind.ClipboardFiles, string.Join("|", entry.FilePaths.Select(static path => path.ToLowerInvariant()))),
                Kind = FavoriteKind.ClipboardFiles,
                Title = entry.Title,
                Subtitle = "Clipboard files",
                Target = string.Join(Environment.NewLine, entry.FilePaths),
            },
            ClipboardHistoryKind.Image when !string.IsNullOrWhiteSpace(entry.ImagePath) => new FavoriteEntry
            {
                Key = BuildKey(FavoriteKind.ClipboardImage, entry.ImageHash ?? entry.ImagePath),
                Kind = FavoriteKind.ClipboardImage,
                Title = entry.Title,
                Subtitle = "Clipboard image",
                Target = entry.ImagePath!,
            },
            _ => null,
        };
    }

    public static FavoriteEntry FromCurrency(decimal amount, string from, string to)
    {
        var normalizedQuery = CurrencyConversionService.NormalizeQuery(amount, from, to);
        return new FavoriteEntry
        {
            Key = BuildKey(FavoriteKind.Currency, normalizedQuery),
            Kind = FavoriteKind.Currency,
            Title = normalizedQuery,
            Subtitle = "Currency conversion",
            Target = normalizedQuery,
        };
    }

    public static FavoriteEntry FromSystemCommand(SystemCommandEntry command)
    {
        return new FavoriteEntry
        {
            Key = BuildKey(FavoriteKind.SystemCommand, command.Name),
            Kind = FavoriteKind.SystemCommand,
            Title = command.Name,
            Subtitle = command.Description,
            Target = command.Name,
        };
    }

    public static SearchAction CreateToggleAction(FavoriteService? favoriteService, FavoriteEntry? favorite)
    {
        if (favoriteService is null || favorite is null)
        {
            return new SearchAction
            {
                Label = "Pin Favorite",
                HintLabel = "Pin",
                Execute = () => { },
            };
        }

        var isFavorite = favoriteService.IsFavorite(favorite.Key);
        return new SearchAction
        {
            Label = isFavorite ? "Unpin Favorite" : "Pin Favorite",
            HintLabel = isFavorite ? "Unpin" : "Pin",
            Execute = () =>
            {
                if (favoriteService.IsFavorite(favorite.Key))
                    favoriteService.Remove(favorite.Key);
                else
                    favoriteService.AddOrUpdate(favorite);
            },
            KeyGesture = "Ctrl+P",
            ClosesLauncher = false,
            RefreshesResults = true,
        };
    }

    private FavoriteEntry? GetEntry(string entryId)
    {
        EnsureLoaded();

        lock (_gate)
        {
            var entry = _entries!.FirstOrDefault(candidate => candidate.Id == entryId);
            return entry is null ? null : CloneEntry(entry);
        }
    }

    private void LaunchEntry(FavoriteEntry entry)
    {
        switch (entry.Kind)
        {
            case FavoriteKind.App:
                ProcessHelper.Launch(entry.Target, asAdmin: false, entry.Arguments, entry.WorkingDirectory);
                break;
            case FavoriteKind.Run:
                _runTargetLauncher.Launch(new RunTarget
                {
                    Title = entry.Title,
                    Command = entry.Target,
                    Subtitle = entry.Subtitle,
                    Kind = entry.RunKind ?? "Command",
                    SupportsRunAsAdmin = entry.SupportsRunAsAdmin,
                    WorkingDirectory = entry.WorkingDirectory,
                }, asAdmin: false);
                break;
            case FavoriteKind.File:
            case FavoriteKind.QuickLink:
                Process.Start(new ProcessStartInfo(entry.Target) { UseShellExecute = true });
                break;
            case FavoriteKind.ClipboardText:
                WpfClipboard.SetText(entry.Target, WpfTextDataFormat.UnicodeText);
                break;
            case FavoriteKind.ClipboardFiles:
                var collection = new StringCollection();
                collection.AddRange(entry.Target.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
                WpfClipboard.SetFileDropList(collection);
                break;
            case FavoriteKind.ClipboardImage:
                if (TryLoadBitmap(entry.Target, out var bitmap))
                    WpfClipboard.SetImage(bitmap);
                break;
            case FavoriteKind.Currency:
                var conversion = _currencyConversionService?
                    .ConvertAsync(entry.Target, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                WpfClipboard.SetText(conversion?.Formatted ?? entry.Target, WpfTextDataFormat.UnicodeText);
                break;
            case FavoriteKind.SystemCommand:
                SystemCommandCatalog.TryExecute(entry.Target);
                break;
        }
    }

    private void RecordUse(string entryId)
    {
        EnsureLoaded();

        lock (_gate)
        {
            var entry = _entries!.FirstOrDefault(candidate => candidate.Id == entryId);
            if (entry is null)
                return;

            entry.UseCount++;
            entry.LastUsedUtc = DateTime.UtcNow;
            SaveUnsafe();
        }
    }

    private void EnsureLoaded()
    {
        if (_entries is not null)
            return;

        lock (_gate)
        {
            if (_entries is not null)
                return;

            if (!File.Exists(_favoritesPath))
            {
                _entries = [];
                EnsureSortOrderUnsafe();
                return;
            }

            try
            {
                var json = File.ReadAllText(_favoritesPath);
                _entries = JsonSerializer.Deserialize<List<FavoriteEntry>>(json) ?? [];
            }
            catch
            {
                _entries = [];
            }

            EnsureSortOrderUnsafe();
        }
    }

    private void SaveUnsafe()
    {
        try
        {
            var json = JsonSerializer.Serialize(_entries, JsonOptions);
            File.WriteAllText(_favoritesPath, json);
        }
        catch
        {
        }
    }

    private FavoriteEntry PrepareFavoriteForStorage(FavoriteEntry favorite)
    {
        var storedFavorite = CloneEntry(favorite);
        if (storedFavorite.Kind != FavoriteKind.ClipboardImage)
            return storedFavorite;

        if (TryCopyFavoriteImage(storedFavorite.Target, out var storedImagePath))
            storedFavorite.Target = storedImagePath;

        return storedFavorite;
    }

    private bool TryCopyFavoriteImage(string sourcePath, out string storedImagePath)
    {
        storedImagePath = "";
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return false;

        try
        {
            var bytes = File.ReadAllBytes(sourcePath);
            if (bytes.Length == 0)
                return false;

            var extension = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".png";

            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            Directory.CreateDirectory(_favoriteImageDirectory);
            storedImagePath = Path.Combine(_favoriteImageDirectory, $"{hash}{extension}");
            if (!File.Exists(storedImagePath))
                File.WriteAllBytes(storedImagePath, bytes);

            return true;
        }
        catch
        {
            storedImagePath = "";
            return false;
        }
    }

    private void DeleteUnusedFavoriteImageUnsafe(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !IsFavoriteImagePath(imagePath))
            return;

        if (_entries!.Any(entry =>
            entry.Kind == FavoriteKind.ClipboardImage &&
            entry.Target.Equals(imagePath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        try
        {
            if (File.Exists(imagePath))
                File.Delete(imagePath);
        }
        catch
        {
        }
    }

    private bool IsFavoriteImagePath(string imagePath)
    {
        try
        {
            var fullImagePath = Path.GetFullPath(imagePath);
            var fullImageDirectory = Path.GetFullPath(_favoriteImageDirectory);
            return fullImagePath.StartsWith(fullImageDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static double GetMatchScore(string query, FavoriteEntry entry)
    {
        var title = FuzzyMatcher.Match(query, entry.Title);
        var subtitle = string.IsNullOrWhiteSpace(entry.Subtitle)
            ? new FuzzyMatchResult(false, 0.0)
            : FuzzyMatcher.Match(query, entry.Subtitle!);
        var target = FuzzyMatcher.Match(query, entry.Target);

        return new[]
        {
            title.IsMatch ? title.Score : 0.0,
            subtitle.IsMatch ? subtitle.Score * 0.8 : 0.0,
            target.IsMatch ? target.Score * 0.65 : 0.0,
        }.Max();
    }

    private static FavoriteEntry CloneEntry(FavoriteEntry entry)
    {
        return new FavoriteEntry
        {
            Id = entry.Id,
            Key = entry.Key,
            Kind = entry.Kind,
            Title = entry.Title,
            Subtitle = entry.Subtitle,
            Target = entry.Target,
            Arguments = entry.Arguments,
            WorkingDirectory = entry.WorkingDirectory,
            RunKind = entry.RunKind,
            IconPath = entry.IconPath,
            IconIndex = entry.IconIndex,
            SupportsRunAsAdmin = entry.SupportsRunAsAdmin,
            SortOrder = entry.SortOrder,
            UseCount = entry.UseCount,
            CreatedUtc = entry.CreatedUtc,
            LastUsedUtc = entry.LastUsedUtc,
        };
    }

    private static string BuildKey(FavoriteKind kind, params string[] parts)
    {
        return $"{kind}:{HashValue(string.Join('\u001F', parts))}";
    }

    private void EnsureSortOrderUnsafe()
    {
        var entries = _entries!;
        if (entries.Count == 0)
            return;

        var needsNormalization =
            entries.Any(static entry => entry.SortOrder <= 0) ||
            entries.Select(static entry => entry.SortOrder).Distinct().Count() != entries.Count;

        if (!needsNormalization)
            return;

        var orderedEntries = entries
            .OrderBy(static entry => entry.SortOrder <= 0 ? 1 : 0)
            .ThenBy(static entry => entry.SortOrder <= 0 ? int.MaxValue : entry.SortOrder)
            .ThenByDescending(static entry => entry.UseCount)
            .ThenByDescending(static entry => entry.LastUsedUtc)
            .ThenBy(static entry => entry.CreatedUtc)
            .ThenBy(static entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var index = 0; index < orderedEntries.Count; index++)
            orderedEntries[index].SortOrder = index + 1;

        SaveUnsafe();
    }

    private static string GetAppSubtitle(AppEntry entry)
    {
        if (entry.ExecutablePath.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase))
            return "Installed app";

        return entry.DisplayPath;
    }

    private static string HashValue(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool TryLoadBitmap(string? imagePath, out BitmapSource bitmap)
    {
        bitmap = null!;
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return false;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(imagePath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            bitmap = image;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
