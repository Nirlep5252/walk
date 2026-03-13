using Velopack;
using Velopack.Sources;

namespace Walk.Services;

public sealed class UpdateService : IDisposable
{
    private static readonly TimeSpan InitialCheckDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PeriodicCheckInterval = TimeSpan.FromHours(6);

    private readonly UpdateManager? _updateManager;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private Task? _backgroundLoop;

    public UpdateService()
    {
        Version = AppVersionService.GetDisplayVersion();
        StatusText = BuildStatusText();

        try
        {
            _updateManager = new UpdateManager(
                new GithubSource(ReleaseInfo.RepositoryUrl, string.Empty, prerelease: false, downloader: null));

            if (_updateManager.CurrentVersion is not null)
            {
                Version = _updateManager.CurrentVersion.ToString();
                StatusText = BuildStatusText();
            }
        }
        catch
        {
            _updateManager = null;
        }
    }

    public event EventHandler<UpdateStatusChangedEventArgs>? StatusChanged;

    public string Version { get; private set; }

    public string StatusText { get; private set; }

    public bool CanCheckForUpdates => _updateManager?.IsInstalled == true;

    public void Start()
    {
        if (!CanCheckForUpdates || _backgroundLoop is not null)
            return;

        _backgroundLoop = Task.Run(() => RunPeriodicChecksAsync(_shutdown.Token));
    }

    public async Task CheckForUpdatesAsync(bool manual = false, CancellationToken cancellationToken = default)
    {
        if (!CanCheckForUpdates)
        {
            if (manual)
                PublishStatus("installer updates are only available in installed builds");

            return;
        }

        if (!await _updateLock.WaitAsync(0, cancellationToken))
            return;

        try
        {
            PublishStatus("checking for updates");

            var update = await _updateManager!.CheckForUpdatesAsync();
            if (update is null)
            {
                PublishStatus(manual ? "already up to date" : null);
                return;
            }

            var targetVersion = update.TargetFullRelease.Version.ToString();
            PublishStatus($"downloading v{targetVersion}");

            await _updateManager.DownloadUpdatesAsync(
                update,
                progress => PublishStatus($"downloading v{targetVersion} ({progress}%)"),
                cancellationToken);

            PublishStatus($"installing v{targetVersion}");

            await _updateManager.WaitExitThenApplyUpdatesAsync(
                update.TargetFullRelease,
                silent: true,
                restart: true,
                Array.Empty<string>());

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                () => System.Windows.Application.Current.Shutdown());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _shutdown.IsCancellationRequested)
        {
        }
        catch
        {
            PublishStatus(manual ? "update failed" : null);
        }
        finally
        {
            _updateLock.Release();
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
        _updateLock.Dispose();
    }

    private async Task RunPeriodicChecksAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(InitialCheckDelay, cancellationToken);
            await CheckForUpdatesAsync(cancellationToken: cancellationToken);

            using var timer = new PeriodicTimer(PeriodicCheckInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
                await CheckForUpdatesAsync(cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private string BuildStatusText(string? detail = null)
    {
        return string.IsNullOrWhiteSpace(detail)
            ? $"v{Version}"
            : $"v{Version} - {detail}";
    }

    private void PublishStatus(string? detail)
    {
        StatusText = BuildStatusText(detail);
        StatusChanged?.Invoke(this, new UpdateStatusChangedEventArgs(StatusText));
    }
}

public sealed class UpdateStatusChangedEventArgs(string statusText) : EventArgs
{
    public string StatusText { get; } = statusText;
}
