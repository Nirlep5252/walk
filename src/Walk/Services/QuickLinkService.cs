using System.IO;
using System.Text;
using System.Text.Json;
using Walk.Helpers;
using Walk.Models;

namespace Walk.Services;

public sealed class QuickLinkService
{
    private readonly object _gate = new();
    private readonly string _quickLinksPath;
    private readonly IQuickLinkLauncher _launcher;
    private List<QuickLinkEntry>? _entries;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public QuickLinkService(string dataDir, IQuickLinkLauncher? launcher = null)
    {
        Directory.CreateDirectory(dataDir);
        _quickLinksPath = Path.Combine(dataDir, "quicklinks.json");
        _launcher = launcher ?? new QuickLinkLauncher();
    }

    public IReadOnlyList<QuickLinkEntry> GetEntries()
    {
        EnsureLoaded();

        lock (_gate)
        {
            return _entries!
                .Select(CloneEntry)
                .ToList();
        }
    }

    public IReadOnlyList<QuickLinkEntry> Search(string query, int maxResults = 20)
    {
        EnsureLoaded();

        var trimmed = query.Trim();
        var limit = Math.Max(1, maxResults);

        lock (_gate)
        {
            if (trimmed.Length == 0)
            {
                return _entries!
                    .OrderByDescending(static entry => entry.UseCount)
                    .ThenByDescending(static entry => entry.LastUsedUtc)
                    .ThenBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(limit)
                    .Select(CloneEntry)
                    .ToList();
            }

            return _entries!
                .Select(entry => (Entry: entry, Score: GetMatchScore(trimmed, entry)))
                .Where(static match => match.Score > 0)
                .OrderByDescending(static match => match.Score)
                .ThenByDescending(static match => match.Entry.UseCount)
                .ThenBy(static match => match.Entry.Name, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .Select(match => CloneEntry(match.Entry))
                .ToList();
        }
    }

    public QuickLinkEntry? FindAlias(string alias)
    {
        EnsureLoaded();

        var normalizedAlias = NormalizeAlias(alias);
        if (normalizedAlias.Length == 0)
            return null;

        lock (_gate)
        {
            var entry = _entries!.FirstOrDefault(candidate =>
                candidate.Alias.Equals(normalizedAlias, StringComparison.OrdinalIgnoreCase));
            return entry is null ? null : CloneEntry(entry);
        }
    }

    public QuickLinkEntry? AddOrUpdate(string name, string target, string? alias = null)
    {
        EnsureLoaded();

        var normalizedName = NormalizeName(name);
        var normalizedTarget = target.Trim();
        var normalizedAlias = NormalizeAlias(alias ?? normalizedName);

        if (normalizedName.Length == 0 || normalizedTarget.Length == 0 || normalizedAlias.Length == 0)
            return null;

        lock (_gate)
        {
            var existing = _entries!.FirstOrDefault(entry =>
                entry.Alias.Equals(normalizedAlias, StringComparison.OrdinalIgnoreCase) ||
                entry.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                existing = new QuickLinkEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    CreatedUtc = DateTime.UtcNow,
                };
                _entries!.Add(existing);
            }

            existing.Name = normalizedName;
            existing.Alias = normalizedAlias;
            existing.Target = normalizedTarget;
            existing.Description = null;
            existing.IsBuiltIn = false;
            SaveUnsafe();
            return CloneEntry(existing);
        }
    }

    public bool Remove(string entryId)
    {
        EnsureLoaded();

        lock (_gate)
        {
            var removed = _entries!.RemoveAll(entry => entry.Id == entryId);
            if (removed > 0)
                SaveUnsafe();

            return removed > 0;
        }
    }

    public void Launch(string entryId, string query)
    {
        var entry = GetEntry(entryId);
        if (entry is null)
            return;

        _launcher.Launch(ResolveTarget(entry, query));
        RecordUse(entry.Id);
    }

    public static string ResolveTarget(QuickLinkEntry entry, string query)
    {
        var rawQuery = query.Trim();
        var escapedQuery = Uri.EscapeDataString(rawQuery);

        return entry.Target
            .Replace("{queryRaw}", rawQuery, StringComparison.OrdinalIgnoreCase)
            .Replace("{query}", escapedQuery, StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeAlias(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_')
                builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    private QuickLinkEntry? GetEntry(string entryId)
    {
        EnsureLoaded();

        lock (_gate)
        {
            var entry = _entries!.FirstOrDefault(candidate => candidate.Id == entryId);
            return entry is null ? null : CloneEntry(entry);
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

            if (!File.Exists(_quickLinksPath))
            {
                _entries = GetDefaultQuickLinks().Select(CloneEntry).ToList();
                SaveUnsafe();
                return;
            }

            try
            {
                var json = File.ReadAllText(_quickLinksPath);
                _entries = JsonSerializer.Deserialize<List<QuickLinkEntry>>(json) ?? [];
            }
            catch
            {
                _entries = GetDefaultQuickLinks().Select(CloneEntry).ToList();
                SaveUnsafe();
            }
        }
    }

    private void SaveUnsafe()
    {
        try
        {
            var json = JsonSerializer.Serialize(_entries, JsonOptions);
            File.WriteAllText(_quickLinksPath, json);
        }
        catch
        {
        }
    }

    private static double GetMatchScore(string query, QuickLinkEntry entry)
    {
        if (entry.Alias.Equals(query, StringComparison.OrdinalIgnoreCase))
            return 1.0;

        if (entry.Alias.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 0.94;

        var nameMatch = FuzzyMatcher.Match(query, entry.Name);
        var aliasMatch = FuzzyMatcher.Match(query, entry.Alias);
        var targetMatch = FuzzyMatcher.Match(query, entry.Target);

        return new[]
        {
            nameMatch.IsMatch ? nameMatch.Score : 0.0,
            aliasMatch.IsMatch ? aliasMatch.Score * 0.96 : 0.0,
            targetMatch.IsMatch ? targetMatch.Score * 0.65 : 0.0,
        }.Max();
    }

    private static string NormalizeName(string value)
    {
        return string.Join(
            " ",
            value
                .Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static QuickLinkEntry CloneEntry(QuickLinkEntry entry)
    {
        return new QuickLinkEntry
        {
            Id = entry.Id,
            Name = entry.Name,
            Alias = entry.Alias,
            Target = entry.Target,
            Description = entry.Description,
            IsBuiltIn = entry.IsBuiltIn,
            UseCount = entry.UseCount,
            CreatedUtc = entry.CreatedUtc,
            LastUsedUtc = entry.LastUsedUtc,
        };
    }

    private static IReadOnlyList<QuickLinkEntry> GetDefaultQuickLinks()
    {
        var now = DateTime.UtcNow;
        return
        [
            new()
            {
                Id = "builtin-google",
                Name = "Google Search",
                Alias = "g",
                Target = "https://www.google.com/search?q={query}",
                Description = "Search Google",
                IsBuiltIn = true,
                CreatedUtc = now,
            },
            new()
            {
                Id = "builtin-github",
                Name = "GitHub Search",
                Alias = "gh",
                Target = "https://github.com/search?q={query}",
                Description = "Search GitHub",
                IsBuiltIn = true,
                CreatedUtc = now,
            },
            new()
            {
                Id = "builtin-youtube",
                Name = "YouTube Search",
                Alias = "yt",
                Target = "https://www.youtube.com/results?search_query={query}",
                Description = "Search YouTube",
                IsBuiltIn = true,
                CreatedUtc = now,
            },
            new()
            {
                Id = "builtin-maps",
                Name = "Google Maps",
                Alias = "maps",
                Target = "https://www.google.com/maps/search/{query}",
                Description = "Search Google Maps",
                IsBuiltIn = true,
                CreatedUtc = now,
            },
            new()
            {
                Id = "builtin-wikipedia",
                Name = "Wikipedia",
                Alias = "wiki",
                Target = "https://en.wikipedia.org/wiki/Special:Search?search={query}",
                Description = "Search Wikipedia",
                IsBuiltIn = true,
                CreatedUtc = now,
            },
            new()
            {
                Id = "builtin-duckduckgo",
                Name = "DuckDuckGo",
                Alias = "ddg",
                Target = "https://duckduckgo.com/?q={query}",
                Description = "Search DuckDuckGo",
                IsBuiltIn = true,
                CreatedUtc = now,
            },
        ];
    }
}
