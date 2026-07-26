using MicroStore.OrderService.Domain.Order.ValueObjects;
using MicroStore.OrderService.DTO;
using MicroStore.OrderService.LockWithHeartBeat;
using MicroStore.OrderService.Persistence.Data;

using StackExchange.Redis;

using System.Text.Json;

namespace MicroStore.OrderService.BackgroundWorkers;

//چون BackgroundService با AddSingleton تزریق میشود سرویس های AddScoped مثل کانتکس نباید به BackgroundService تزریق شوند و فقط میتوان آنها را با استفاده از IServiceScopeFactory بگیریم
public sealed class OrderQueueWorkerWithHeartBeat : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDistributedLockServiceWithHeartBeat _distributedLockServiceWithHeartBeat;
    private readonly ILogger<OrderQueueWorker> _logger;

    public OrderQueueWorkerWithHeartBeat(IConnectionMultiplexer redis,IServiceScopeFactory scopeFactory, IDistributedLockServiceWithHeartBeat distributedLockServiceWithHeartBeat, ILogger<OrderQueueWorker> logger)
    {
        _redis = redis;
        _scopeFactory = scopeFactory;
        _distributedLockServiceWithHeartBeat = distributedLockServiceWithHeartBeat;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var db = _redis.GetDatabase();

        while (!stoppingToken.IsCancellationRequested)
        {
            //اولین آیتم رو از صف میخونه و حذف میکنه
            RedisValue value =await db.ListLeftPopAsync("order-queue");

            if (!value.HasValue)
            {
                await Task.Delay(500, stoppingToken);
                continue;
            }

            var model =
                JsonSerializer.Deserialize<OrderDto>(value!);

            if (model == null)
                continue;

            await ProcessOrderAsync(model);
        }
    }

    private async Task ProcessOrderAsync(OrderDto orderDto)
    {
        // دریافت شیء Database از Redis
        var db = _redis.GetDatabase();

        // ساخت کلید قفل برای این محصول
        // مثال: lock:product:1001
        var lockKey = $"lock:product:{orderDto.ProductId}";

        await using var lockHandle = await _distributedLockServiceWithHeartBeat.AcquireAsync(lockKey);

        // اگر قفل در اختیار درخواست دیگری باشد
        //اگر از صف RabbitMQ استفاده کنیم نیازی به برگرداندن سفارش به صف نیست چون از حذف حذف نشده است
        if (lockHandle == null)
        {
            // هنوز شخص دیگری در حال پردازش همین محصول است.
            // دوباره به انتهای صف برگردان.
            await db.ListRightPushAsync(
                "order-queue",
                JsonSerializer.Serialize(orderDto));

            return;
        }

        try
        {
            // -------------------------------  
            // از اینجا به بعد قفل با موفقیت گرفته شده است.  
            // -------------------------------  

            // کلید موجودی محصول  
            var stockKey = $"product:stock:{orderDto.ProductId}";

            //if(await db.KeyExistsAsync(stockKey))

            //// خواندن موجودی از Redis  
            var stockValue = await db.StringGetAsync(stockKey);

            // اگر محصول موجودی نداشته باشد  
            if (!stockValue.HasValue)
            {
                _logger.LogWarning("Product not found.");
                return;
            }

            // تبدیل مقدار موجودی به عدد  
            int currentStock = int.Parse(stockValue);

            // بررسی کافی بودن موجودی  
            if (currentStock < orderDto.Quantity)
            {
                _logger.LogWarning(
                    "Insufficient stock for {ProductId}",
                    orderDto.ProductId);

                return;
            }

            // محاسبه موجودی جدید  
            currentStock -= orderDto.Quantity;

            // ذخیره موجودی در Redis برای استفاده در ورکر OrderQueueWorker
            await db.StringSetAsync(
                stockKey,
                currentStock,
                TimeSpan.FromMinutes(30),
                When.NotExists);

            using var scope = _scopeFactory.CreateScope();

            var context =
                scope.ServiceProvider
                     .GetRequiredService<AppDbContext>();

            // اطلاعات سفارش را تکمیل می‌کنیم  
            orderDto.ID = Guid.NewGuid();

            Address address = new Address("Iran", "Tehran", "Tehran", "street", "123456789");


            // ساخت موجودیت سفارش  
            var order = new MicroStore.OrderService.Domain.Order.AggregateRoot.Order(orderDto.ID, address);

            var money = new Money(1000000000, "IRR");
            order.AddItem(Guid.NewGuid(), "LapTop", money, 2);

            context.Orders.Add(order);

            await context.SaveChangesAsync();

            // دریافت Publisher مربوط به Redis Pub/Sub  
            //برای انتشار رویداد RabbitMQ بهتره
            await _redis
                .GetSubscriber()
                .PublishAsync(
                    "order-placed",
                    JsonSerializer.Serialize(orderDto));

            _logger.LogInformation(
                "Queued order {OrderId} processed.",
                orderDto.ID);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing order.");
            return;
        }
    }
}
