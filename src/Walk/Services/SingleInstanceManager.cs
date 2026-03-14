namespace Walk.Services;

public sealed class SingleInstanceManager : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly CancellationTokenSource _disposeCts = new();
    private Task? _listenerTask;

    public SingleInstanceManager(string appId)
    {
        var normalizedAppId = NormalizeName(appId);
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{normalizedAppId}.Mutex", out var createdNew);
        _activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            $@"Local\{normalizedAppId}.Activate");
        IsPrimaryInstance = createdNew;
    }

    public bool IsPrimaryInstance { get; }

    public bool SignalPrimaryInstance()
    {
        if (IsPrimaryInstance)
            return false;

        try
        {
            return _activationEvent.Set();
        }
        catch
        {
            return false;
        }
    }

    public void StartListening(Action onActivation)
    {
        if (!IsPrimaryInstance || _listenerTask is not null)
            return;

        _listenerTask = Task.Run(() =>
        {
            using var registration = _disposeCts.Token.Register(() =>
            {
                try
                {
                    _activationEvent.Set();
                }
                catch
                {
                }
            });

            while (!_disposeCts.IsCancellationRequested)
            {
                _activationEvent.WaitOne();
                if (_disposeCts.IsCancellationRequested)
                    break;

                try
                {
                    onActivation();
                }
                catch
                {
                    // Ignore activation handler failures so future activations still work.
                }
            }
        });
    }

    public void Dispose()
    {
        _disposeCts.Cancel();

        try
        {
            _listenerTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
        }

        _activationEvent.Dispose();

        if (IsPrimaryInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
            }
        }

        _mutex.Dispose();
        _disposeCts.Dispose();
    }

    private static string NormalizeName(string appId)
    {
        var buffer = new char[appId.Length];
        for (var i = 0; i < appId.Length; i++)
        {
            var current = appId[i];
            buffer[i] = char.IsLetterOrDigit(current) ? current : '_';
        }

        return new string(buffer);
    }
}
