namespace LockFlow.OrderService.Lock;
public interface ILockService
{
    Task<LockHandle?> AcquireAsync(string key);

    Task<bool> RenewAsync(LockHandle handle);

    Task<bool> ReleaseAsync(LockHandle handle);
}
