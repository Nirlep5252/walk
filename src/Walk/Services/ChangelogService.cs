using System.IO;
using System.Text.Json;

namespace Walk.Services;

public sealed class ChangelogEntry
{
    public string Version { get; set; } = string.Empty;
    public string Markdown { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ChangelogState
{
    public ChangelogEntry? LatestEntry { get; set; }
    public ChangelogEntry? PendingEntry { get; set; }
}

public sealed class ChangelogService
{
    private readonly string _statePath;

    public ChangelogService(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _statePath = Path.Combine(dataDir, "changelog-state.json");
    }

    public async Task StageAsync(ChangelogEntry entry, CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken);
        state.PendingEntry = entry;
        await SaveStateAsync(state, cancellationToken);
    }

    public async Task<ChangelogEntry?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken);
        return state.LatestEntry;
    }

    public async Task<ChangelogEntry?> GetPendingAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken);
        if (state.PendingEntry is null)
            return null;

        if (!string.Equals(state.PendingEntry.Version, currentVersion, StringComparison.OrdinalIgnoreCase))
            return null;

        return state.PendingEntry;
    }

    public async Task MarkPendingAsSeenAsync(string version, CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken);
        if (state.PendingEntry is null ||
            !string.Equals(state.PendingEntry.Version, version, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        state.LatestEntry = state.PendingEntry;
        state.PendingEntry = null;
        await SaveStateAsync(state, cancellationToken);
    }

    private async Task<ChangelogState> LoadStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_statePath))
            return new ChangelogState();

        try
        {
            var json = await File.ReadAllTextAsync(_statePath, cancellationToken);
            return JsonSerializer.Deserialize<ChangelogState>(json) ?? new ChangelogState();
        }
        catch
        {
            return new ChangelogState();
        }
    }

    private async Task SaveStateAsync(ChangelogState state, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_statePath, json, cancellationToken);
    }
}
