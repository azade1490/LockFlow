using LockFlow.OrderService.Domain.Order.ValueObjects;
using LockFlow.OrderService.DTO;
using LockFlow.OrderService.Lock;
using LockFlow.OrderService.Persistence.Data;

using StackExchange.Redis;

using System.Text.Json;

namespace LockFlow.OrderService.BackgroundWorkers;

//چون BackgroundService با AddSingleton تزریق میشود سرویس های AddScoped مثل کانتکس نباید به BackgroundService تزریق شوند و فقط میتوان آنها را با استفاده از IServiceScopeFactory بگیریم
public sealed class OrderQueueWorker : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILockService _lockService;
    private readonly ILogger<OrderQueueWorker> _logger;

    public OrderQueueWorker(IConnectionMultiplexer redis,IServiceScopeFactory scopeFactory, ILockService lockService, ILogger<OrderQueueWorker> logger)
    {
        _redis = redis;
        _scopeFactory = scopeFactory;
        _lockService = lockService;
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

        var lockHandle = await _lockService.AcquireAsync(lockKey);

        // اگر قفل در اختیار درخواست دیگری باشد
        //اگر از صف RabbitMQ استفاده کنیم نیازی به برگرداندن سفارش به صف نیست چون از صف حذف نشده است
        if (lockHandle == null)
        {
            // هنوز شخص دیگری در حال پردازش همین محصول است.
            // دوباره به انتهای صف برگردان.
            await db.ListRightPushAsync(
                "order-queue",
                JsonSerializer.Serialize(orderDto));

            return;
        }

        using var cts = new CancellationTokenSource();
        Task? heartbeatTask = null;

        try
        {
            // -------------------------------  
            // از اینجا به بعد قفل با موفقیت گرفته شده است.  
            // -------------------------------  

            // 👇 شروع Heartbeat
            heartbeatTask = Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

                try
                {
                    while (await timer.WaitForNextTickAsync(cts.Token))
                    {
                        bool renewed = await _lockService.RenewAsync(lockHandle);

                        if (!renewed)
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    //وقتی cts.Cancel() اجرا میشود WaitForNextTickAsync(cts.Token) استثنا پرتاب میکند.
                    // طبیعی است و نیازی به لاگ ندارد.
                }
            });
            // پایان Heartbeat
            var stockKey = $"product:stock:{orderDto.ProductId}";

            //// خواندن موجودی از Redis  
            var stockValue = await db.StringGetAsync(stockKey);

            // اگر محصول موجودی نداشته باشد  
            if (!stockValue.HasValue)
            {
                _logger.LogWarning(
                "Stock not found for product {ProductId}",
                orderDto.ProductId);
                return;
            }

            int currentStock = int.Parse(stockValue);

            // بررسی کافی بودن موجودی  
            if (currentStock < orderDto.Quantity)
            {
                _logger.LogWarning(
                    "Insufficient stock for ProductId: {ProductId}",
                    orderDto.ProductId);

                return;
            }

            // محاسبه موجودی جدید  
            currentStock -= orderDto.Quantity;

            // ذخیره موجودی در Redis برای استفاده در ورکر OrderQueueWorker
            await db.StringSetAsync(
                stockKey,
                currentStock,
                TimeSpan.FromSeconds(120)
                );

            using var scope = _scopeFactory.CreateScope();

            var context =
                scope.ServiceProvider
                     .GetRequiredService<AppDbContext>();

            Address address = new Address("Iran", "Tehran", "Tehran", "street", "123456789");
            var order = new LockFlow.OrderService.Domain.Order.AggregateRoot.Order(address);
            var money = new Money(1000000000, "IRR");
            order.AddItem(Guid.NewGuid(), "LapTop", money, orderDto.Quantity);

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
                order.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing order.");
            return;

        }
        finally
        {
            // این بخش در هر صورت اجرا می‌شود
            //(چه درخواست موفق باشد چه خطا رخ دهد)

            cts.Cancel();

            if (heartbeatTask != null)
            {
                try
                {
                    //متوقف کردن heartbeatTask
                    await heartbeatTask;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Heartbeat task failed.");
                }
            }
                await _lockService.ReleaseAsync(lockHandle);
        }
    }
}
