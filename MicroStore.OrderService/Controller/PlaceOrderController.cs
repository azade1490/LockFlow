using Microsoft.AspNetCore.Mvc;

using MicroStore.OrderService.Application.Common.Messaging;
using MicroStore.OrderService.Application.Events;
using MicroStore.OrderService.Domain.Order.ValueObjects;
using MicroStore.OrderService.DTO;
using MicroStore.OrderService.Lock;
using MicroStore.OrderService.Persistence.Data;

using StackExchange.Redis;

using System.Text.Json;

namespace MicroStore.OrderService.Controllers;

[ApiController]
[Route("[controller]/[Action]")]
public class PlaceOrderController : ControllerBase
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IEventBus _eventBus;
    private readonly ILogger<Domain.Order.AggregateRoot.Order> _logger;
    private readonly AppDbContext _context;
    private readonly IDistributedLockService _distributedLockService;
    public PlaceOrderController(ILogger<Domain.Order.AggregateRoot.Order> logger, IConnectionMultiplexer connectionMultiplexer, AppDbContext appDbContext, IDistributedLockService distributedLockService, IEventBus eventBus)
    {
        _logger = logger;
        _redis = connectionMultiplexer;
        _context = appDbContext;
        _distributedLockService = distributedLockService;
        _eventBus = eventBus;
    }

    [HttpPost]
    public async Task<IActionResult> PlaceOrder(OrderDto orderDto)
    {
        // دریافت شیء Database از Redis
        var db = _redis.GetDatabase();

        // ساخت کلید قفل برای این محصول
        // مثال: lock:product:1001
        var lockKey = $"lock:product:{orderDto.ProductId}";

        var lockHandle = await _distributedLockService.AcquireAsync(lockKey);

        // اگر قفل در اختیار درخواست دیگری باشد  
        if (lockHandle == null)
        {
            // قرار دادن سفارش در صف Redis  
            // تا بعداً توسط Worker پردازش شود.  
            await db.ListRightPushAsync("order-queue", JsonSerializer.Serialize(orderDto));

            // ثبت لاگ  
            _logger.LogInformation(
                "Order {OrderId} queued because lock for ProductId {ProductId} was unavailable.",
                orderDto.ID,
                orderDto.ProductId);

            // پاسخ به کاربر  
            return Accepted(new
            {
                orderDto.ID,
                Message = "Your order has been queued and will be processed shortly."
            });
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
            //پایان Heartbeat

            //باید از سرویس Inventory خوانده شود
            var stockKey = $"product:stock:{orderDto.ProductId}";

            //if(await db.KeyExistsAsync(stockKey))

            //// خواندن موجودی از Redis  
            var stockValue = await db.StringGetAsync(stockKey);

            if (!stockValue.HasValue)
            {
                _logger.LogWarning(
                "Stock not found for product {ProductId}",
                orderDto.ProductId);
                return BadRequest((
                "Stock not found for product {ProductId}",
                orderDto.ProductId));
            }

            // تبدیل مقدار موجودی به عدد  
            var currentStock = int.Parse(stockValue);

            // بررسی کافی بودن موجودی  
            if (currentStock < orderDto.Quantity)
            {
                _logger.LogWarning(
                    "Insufficient stock for ProductId: {ProductId}",
                    orderDto.ProductId);

                return BadRequest((
                    "Insufficient stock for ProductId: {ProductId}",
                    orderDto.ProductId));
            }

            // محاسبه موجودی جدید  
            currentStock -= orderDto.Quantity;

            // ذخیره موجودی در Redis برای استفاده در ورکر OrderQueueWorker
            await db.StringSetAsync(
                stockKey,
                currentStock);

            // اطلاعات سفارش را تکمیل می‌کنیم  
            orderDto.ID = Guid.NewGuid();

            Address address = new Address("Iran", "Tehran", "Tehran", "street", "123456789");

            // ساخت موجودیت سفارش  
            var order = new MicroStore.OrderService.Domain.Order.AggregateRoot.Order(orderDto.ID, address);

            Money money = new Money(1000000000, "IRR");
            order.AddItem(Guid.NewGuid(), "LapTop", money, 2);

            // ذخیره سفارش در پایگاه داده  
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // دریافت Publisher مربوط به Redis Pub/Sub  
            //برای انتشار رویداد RabbitMQ بهتره
            await _redis
                .GetSubscriber()
                .PublishAsync(
                    "order-placed",
                    JsonSerializer.Serialize(orderDto));

            var @event = new OrderCreatedEvent(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    5000);
            await _eventBus.PublishAsync(@event);


            // پاسخ موفق  
            return Ok(new
            {
                orderDto.ID,
                Message = ("order { OrderId} processed.",order.OrderId)
            });

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing order.");
            return StatusCode(500, "Internal server error.");
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
