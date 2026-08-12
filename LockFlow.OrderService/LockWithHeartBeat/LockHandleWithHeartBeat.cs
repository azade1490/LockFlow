namespace LockFlow.OrderService.LockWithHeartBeat;

//بهترین طراحی این است که LockHandle خودش مسئول Heartbeat باشد. در این صورت Controller و Worker اصلاً از وجود Timer خبر ندارند.
public sealed class LockHandleWithHeartBeat : IAsyncDisposable
{
    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _cts;
    private readonly IDistributedLockServiceWithHeartBeat _service;

    internal LockHandleWithHeartBeat(IDistributedLockServiceWithHeartBeat service,string key,string value,TimeSpan expiry)
    {
        _service = service;

        Key = key;
        Value = value;
        Expiry = expiry;

        _cts = new CancellationTokenSource();

        _timer = new PeriodicTimer(
            TimeSpan.FromSeconds(5));

        StartHeartbeat();
    }

    public string Key { get; }

    public string Value { get; }

    public TimeSpan Expiry { get; }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();

        _timer.Dispose();
        
        await _service.ReleaseAsync(this);
    }
    private void StartHeartbeat()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                while (await _timer.WaitForNextTickAsync(_cts.Token))
                {
                    bool ok =
                        await _service.RenewAsync(this);

                    if (!ok)
                    {
                        //Lock از دست رفت
                        _cts.Cancel();
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

}
