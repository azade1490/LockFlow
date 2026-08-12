using StackExchange.Redis;

namespace LockFlow.OrderService.LockWithHeartBeat;
public sealed class DistributedLockServiceWithHeartBeat
    : IDistributedLockServiceWithHeartBeat
{
    private readonly IConnectionMultiplexer _redis;

    public DistributedLockServiceWithHeartBeat(
        IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<LockHandleWithHeartBeat?> AcquireAsync(string key)
    {
        var db = _redis.GetDatabase();

        // مقدار یکتا برای قفل
        // از این مقدار هنگام آزاد کردن قفل استفاده می‌کنیم
        // تا مطمئن شویم قفل متعلق به همین درخواست است.
        string value = Guid.NewGuid().ToString();

        // مدت زمانی که یک کلید قبل از حذف خودکار زنده می ماند TTL یا Time To Live
        // اگر برنامه کرش کند، قفل پس از 10 ثانیه خودکار آزاد می‌شود.
        var expiry = TimeSpan.FromSeconds(10);

        // تلاش برای گرفتن قفل
        //با اضافه کردن When.NotExists فقط در صورتی موفق می‌شود که قبلاً قفلی وجود نداشته باشد
        bool success = await db.StringSetAsync(
            key,
            value,
            expiry,
            When.NotExists);

        if (!success)
            return null;

        return new LockHandleWithHeartBeat(
            this,
            key,
            value,
            expiry
        );
    }

    public async Task<bool> RenewAsync(LockHandleWithHeartBeat handle)
    {
        var db = _redis.GetDatabase();

        // خواندن مقدار فعلی قفل
        var currentValue = await db.StringGetAsync(handle.Key);
        // فقط اگر این درخواست مالک قفل باشد،  
        // قفل تمدید می‌شود.  
        if (currentValue == handle.Value)
        {
            return await db.KeyExpireAsync(handle.Key, TimeSpan.FromSeconds(10));
        }
        return false;
    }

    public async Task<bool> ReleaseAsync(LockHandleWithHeartBeat handle)
    {
        var db = _redis.GetDatabase();

        // خواندن مقدار فعلی قفل
        var currentValue = await db.StringGetAsync(handle.Key);
        // فقط اگر این درخواست مالک قفل باشد،  
        // قفل آزاد می‌شود.  
        if (currentValue == handle.Value)
        {
            return await db.KeyDeleteAsync(handle.Key);
        }
        return false;
    }
}
