using LockFlow.OrderService.Lock;

namespace LockFlow.OrderService.LockWithHeartBeat;

//بهترین طراحی این است که LockHandle خودش مسئول Heartbeat باشد. در این صورت Controller و Worker اصلاً از وجود Timer خبر ندارند.
public sealed class LockHandleWithHeartBeat : IAsyncDisposable
{
    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _cts;
    private readonly IDistributedLockServiceWithHeartBeat _service;

    internal LockHandleWithHeartBeat(IDistributedLockServiceWithHeartBeat service, string key, string value, TimeSpan expiry)
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
    private Task? heartbeatTask = null;

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();

        if (heartbeatTask != null)
        {
            try
            {
                //متوقف کردن heartbeatTask
                await heartbeatTask;
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Heartbeat task failed.");
            }
        }

        _timer.Dispose();
        
        try
        {
            await _service.ReleaseAsync(this);
        }
        catch (Exception ex)
        {
            //_logger.LogError(ex, "Error releasing Redis lock.");
        }
    }
    private void StartHeartbeat()
    {
        heartbeatTask = Task.Run(async () =>
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
                //وقتی cts.Cancel() اجرا میشود WaitForNextTickAsync(cts.Token) استثنا پرتاب میکند.
                // طبیعی است و نیازی به لاگ ندارد.
            }
        });
    }
}
