using System.IO;
using System.Text.Json;
using Walk.Models;

namespace Walk.Services;

public sealed class RunHistoryService
{
    private readonly object _gate = new();
    private readonly string _historyPath;
    private List<RunHistoryEntry>? _entries;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public RunHistoryService(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _historyPath = Path.Combine(dataDir, "run-history.json");
    }

    public IReadOnlyList<RunHistoryEntry> GetEntries()
    {
        EnsureLoaded();

        lock (_gate)
        {
            return _entries!
                .Select(CloneEntry)
                .ToList();
        }
    }

    public void RecordLaunch(string query, RunTarget target)
    {
        EnsureLoaded();

        lock (_gate)
        {
            var entries = _entries!;
            var existing = entries.FirstOrDefault(entry =>
                entry.Command.Equals(target.Command, StringComparison.OrdinalIgnoreCase) &&
                entry.Kind.Equals(target.Kind, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                existing = new RunHistoryEntry();
                entries.Add(existing);
            }

            existing.Title = target.Title;
            existing.Command = target.Command;
            existing.Subtitle = target.Subtitle;
            existing.Kind = target.Kind;
            existing.SupportsRunAsAdmin = target.SupportsRunAsAdmin;
            existing.FileLocationPath = target.FileLocationPath;
            existing.LastQuery = string.IsNullOrWhiteSpace(query) ? existing.LastQuery : query.Trim();
            existing.LaunchCount++;
            existing.LastUsedUtc = DateTime.UtcNow;

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

            if (!File.Exists(_historyPath))
            {
                _entries = [];
                return;
            }

            try
            {
                var json = File.ReadAllText(_historyPath);
                _entries = JsonSerializer.Deserialize<List<RunHistoryEntry>>(json) ?? [];
            }
            catch
            {
                _entries = [];
            }
        }
    }

    private void SaveUnsafe()
    {
        try
        {
            var json = JsonSerializer.Serialize(_entries, JsonOptions);
            File.WriteAllText(_historyPath, json);
        }
        catch
        {
            // Ignore persistence errors so launching still succeeds.
        }
    }

    private static RunHistoryEntry CloneEntry(RunHistoryEntry entry)
    {
        return new RunHistoryEntry
        {
            Title = entry.Title,
            Command = entry.Command,
            Subtitle = entry.Subtitle,
            Kind = entry.Kind,
            SupportsRunAsAdmin = entry.SupportsRunAsAdmin,
            FileLocationPath = entry.FileLocationPath,
            LastQuery = entry.LastQuery,
            LaunchCount = entry.LaunchCount,
            LastUsedUtc = entry.LastUsedUtc,
        };
    }
}
