using StackExchange.Redis;

namespace LockFlow.OrderService.LockWithHeartBeat;
public sealed class LockServiceWithHeartBeat
    : ILockServiceWithHeartBeat
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<Domain.Order.AggregateRoot.Order> _logger;

    public LockServiceWithHeartBeat(
        IConnectionMultiplexer redis, ILogger<Domain.Order.AggregateRoot.Order> logger)
    {
        _redis = redis;
        _logger = logger;
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
            return await db.KeyExpireAsync(handle.Key, handle.Expiry);
        }
        //بهتر است StringGetAsync و KeyExpireAsync به صورت اتمیک انجام شود تا از Race Condition جلوگیری شود

        _logger.LogWarning("Lock lost.");

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
        //بهتر است StringGetAsync و KeyDeleteAsync به صورت اتمیک انجام شود تا از Race Condition جلوگیری شود

        _logger.LogWarning("Lock release failed.");

        return false;
    }
}
