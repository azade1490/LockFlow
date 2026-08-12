namespace LockFlow.OrderService.LockWithHeartBeat;
public interface IDistributedLockServiceWithHeartBeat
{
    Task<LockHandleWithHeartBeat?> AcquireAsync(string key);

    Task<bool> RenewAsync(LockHandleWithHeartBeat handle);

    Task<bool> ReleaseAsync(LockHandleWithHeartBeat handle);
}
