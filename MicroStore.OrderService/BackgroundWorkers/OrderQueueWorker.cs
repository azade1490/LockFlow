using MicroStore.OrderService.Domain.Order.ValueObjects;
using MicroStore.OrderService.DTO;
using MicroStore.OrderService.Lock;
using MicroStore.OrderService.Persistence.Data;

using StackExchange.Redis;

using System.Text.Json;

namespace MicroStore.OrderService.BackgroundWorkers;

//چون BackgroundService با AddSingleton تزریق میشود سرویس های AddScoped مثل کانتکس نباید به BackgroundService تزریق شوند و فقط میتوان آنها را با استفاده از IServiceScopeFactory بگیریم
public sealed class OrderQueueWorker : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDistributedLockService _distributedLockService;
    private readonly ILogger<OrderQueueWorker> _logger;

    public OrderQueueWorker(IConnectionMultiplexer redis,IServiceScopeFactory scopeFactory, IDistributedLockService distributedLockService, ILogger<OrderQueueWorker> logger)
    {
        _redis = redis;
        _scopeFactory = scopeFactory;
        _distributedLockService = distributedLockService;
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

        var lockHandle = await _distributedLockService.AcquireAsync(lockKey);

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

        try
        {
            // -------------------------------  
            // از اینجا به بعد قفل با موفقیت گرفته شده است.  
            // -------------------------------  

            // 👇 اینجا Heartbeat را شروع می‌کنید
            using var cts = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

                while (await timer.WaitForNextTickAsync(cts.Token))
                {
                    bool renewed = await _distributedLockService.RenewAsync(lockHandle);

                    if (!renewed)
                    {
                        _logger.LogWarning("Lock lost.");
                        break;
                    }
                }
            });
          cts.Cancel();  
            //پایان Heartbeat

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
                    "Insufficient stock for {ProductId}",
                    orderDto.ProductId);

                return;
            }

            // محاسبه موجودی جدید  
            currentStock -= orderDto.Quantity;

            // ذخیره موجودی در Redis برای استفاده در ورکر OrderQueueWorker
            await db.StringSetAsync(
                stockKey,
                currentStock
                );

            using var scope = _scopeFactory.CreateScope();

            var context =
                scope.ServiceProvider
                     .GetRequiredService<AppDbContext>();

            Address address = new Address("Iran", "Tehran", "Tehran", "street", "123456789");
            var order = new MicroStore.OrderService.Domain.Order.AggregateRoot.Order(address);
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
            try
            {
                await _distributedLockService.ReleaseAsync(lockHandle);
            }
            catch (Exception ex)
            {
                // اگر آزاد کردن قفل با خطا مواجه شود،  
                // فقط لاگ ثبت می‌شود.  
                _logger.LogError(ex, "Error releasing Redis lock.");
            }
        }
    }
}
