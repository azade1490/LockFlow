namespace LockFlow.OrderService.LockWithHeartBeat;
public interface ILockServiceWithHeartBeat
{
    Task<LockHandleWithHeartBeat?> AcquireAsync(string key);

    Task<bool> RenewAsync(LockHandleWithHeartBeat handle);

    Task<bool> ReleaseAsync(LockHandleWithHeartBeat handle);
}
