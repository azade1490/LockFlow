using Microsoft.AspNetCore.Mvc;

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
    private readonly ILogger<Domain.Order.AggregateRoot.Order> _logger;
    private readonly AppDbContext _context;
    private readonly IDistributedLockService _distributedLockService;
    public PlaceOrderController(ILogger<Domain.Order.AggregateRoot.Order> logger, IConnectionMultiplexer connectionMultiplexer, AppDbContext appDbContext, IDistributedLockService distributedLockService)
    {
        _logger = logger;
        _redis = connectionMultiplexer;
        _context = appDbContext;
        _distributedLockService = distributedLockService;
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

            _logger.LogInformation(
                "Order {OrderId} queued because lock for ProductId {ProductId} was unavailable.",
                orderDto.ID,
                orderDto.ProductId);

            return Accepted(new
            {
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

            var stockKey = $"product:stock:{orderDto.ProductId}";

            // مقدار stock باید از سرویس Inventory خوانده شود ولی اینجا برای تست مقدار stock را در Redis ذخیره میکنیم
            if (!await db.KeyExistsAsync(stockKey))
            {
                await db.StringSetAsync(
                    stockKey,
                    100
                    );
            }

            //// خواندن موجودی از Redis  
            var stockValue = await db.StringGetAsync(stockKey);

            if (!stockValue.HasValue)
            {
                _logger.LogWarning(
                "Stock not found for product {ProductId}",
                orderDto.ProductId);
                
                return BadRequest(new ProblemDetails
                {
                    Detail = $"Stock not found for product {orderDto.ProductId}"
                });
            }

            var currentStock = int.Parse(stockValue);

            // بررسی کافی بودن موجودی  
            if (currentStock < orderDto.Quantity)
            {
                _logger.LogWarning(
                    "Insufficient stock for ProductId: {ProductId}",
                    orderDto.ProductId);

                return BadRequest(new ProblemDetails
                {
                    Detail = $"Insufficient stock for ProductId: {orderDto.ProductId}"
                });
            }

            // محاسبه موجودی جدید  
            currentStock -= orderDto.Quantity;

            // ذخیره موجودی در Redis برای استفاده در ورکر OrderQueueWorker
            await db.StringSetAsync(
                stockKey,
                currentStock);
                
            orderDto.ID = Guid.NewGuid();
            Address address = new Address("Iran", "Tehran", "Tehran", "street", "123456789");
            var order = new MicroStore.OrderService.Domain.Order.AggregateRoot.Order(orderDto.ID, address);
            Money money = new Money(1000000000, "IRR");
            order.AddItem(Guid.NewGuid(), "LapTop", money, orderDto.Quantity);

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // دریافت Publisher مربوط به Redis Pub/Sub  
            //برای انتشار رویداد RabbitMQ بهتره
            await _redis
                .GetSubscriber()
                .PublishAsync(
                    "order-placed",
                    JsonSerializer.Serialize(orderDto));
            
            return Ok(new
            {
                orderDto.ID,
                Message = ("order { OrderId} processed.",order.Id)
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
