namespace Walk.Services;

public sealed class ChangelogRecoveryService
{
    private readonly ChangelogService _changelogService;
    private readonly ReleaseNotesService _releaseNotesService;

    public ChangelogRecoveryService(ChangelogService changelogService, ReleaseNotesService releaseNotesService)
    {
        _changelogService = changelogService;
        _releaseNotesService = releaseNotesService;
    }

    public async Task EnsureCurrentVersionPendingAsync(string version, CancellationToken cancellationToken = default)
    {
        if (!CanRecover(version))
            return;

        if (await _changelogService.GetPendingAsync(version, cancellationToken) is not null)
            return;

        var latest = await _changelogService.GetLatestAsync(cancellationToken);
        if (MatchesVersion(latest, version))
            return;

        var entry = await TryCreateEntryAsync(version, cancellationToken);
        if (entry is null)
            return;

        await _changelogService.StageAsync(entry, cancellationToken);
    }

    public async Task<ChangelogEntry?> GetLatestAvailableForVersionAsync(string version, CancellationToken cancellationToken = default)
    {
        if (!CanRecover(version))
            return await _changelogService.GetLatestAsync(cancellationToken);

        var pending = await _changelogService.GetPendingAsync(version, cancellationToken);
        if (pending is not null)
            return pending;

        var latest = await _changelogService.GetLatestAsync(cancellationToken);
        if (MatchesVersion(latest, version))
            return latest;

        var entry = await TryCreateEntryAsync(version, cancellationToken);
        if (entry is null)
            return latest;

        await _changelogService.SaveLatestAsync(entry, cancellationToken);
        return entry;
    }

    private async Task<ChangelogEntry?> TryCreateEntryAsync(string version, CancellationToken cancellationToken)
    {
        var markdown = await _releaseNotesService.TryGetReleaseNotesAsync(version, cancellationToken);
        if (string.IsNullOrWhiteSpace(markdown))
            return null;

        return new ChangelogEntry
        {
            Version = version,
            Markdown = markdown,
            RecordedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static bool CanRecover(string version)
    {
        return !string.IsNullOrWhiteSpace(version) &&
               !string.Equals(version, AppVersionService.DevelopmentModeVersion, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesVersion(ChangelogEntry? entry, string version)
    {
        return entry is not null &&
               !string.IsNullOrWhiteSpace(entry.Markdown) &&
               string.Equals(entry.Version, version, StringComparison.OrdinalIgnoreCase);
    }
}
