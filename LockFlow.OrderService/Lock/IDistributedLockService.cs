namespace LockFlow.OrderService.Lock;
public interface IDistributedLockService
{
    Task<LockHandle?> AcquireAsync(string key);

    Task<bool> RenewAsync(LockHandle handle);

    Task<bool> ReleaseAsync(LockHandle handle);
}
