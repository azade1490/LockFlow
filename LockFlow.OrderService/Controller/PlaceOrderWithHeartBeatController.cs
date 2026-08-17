using Microsoft.AspNetCore.Mvc;

using LockFlow.OrderService.Domain.Order.ValueObjects;
using LockFlow.OrderService.DTO;
using LockFlow.OrderService.LockWithHeartBeat;
using LockFlow.OrderService.Persistence.Data;

using StackExchange.Redis;

using System.Text.Json;

namespace LockFlow.OrderService.Controllers;

[ApiController]
[Route("[controller]/[Action]")]
public class PlaceOrderWithHeartBeatController : ControllerBase
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<Domain.Order.AggregateRoot.Order> _logger;
    private readonly AppDbContext _context;
    private readonly ILockServiceWithHeartBeat _lockServiceWithHeartBeat;
    public PlaceOrderWithHeartBeatController(ILogger<Domain.Order.AggregateRoot.Order> logger, IConnectionMultiplexer connectionMultiplexer, AppDbContext appDbContext, ILockServiceWithHeartBeat lockServiceWithHeartBeat)
    {
        _logger = logger;
        _redis = connectionMultiplexer;
        _context = appDbContext;
        _lockServiceWithHeartBeat = lockServiceWithHeartBeat;
    }

    [HttpPost]
    public async Task<IActionResult> PlaceOrder(OrderDto orderDto)
    {
        // دریافت شیء Database از Redis
        var db = _redis.GetDatabase();

        // ساخت کلید قفل برای این محصول
        // مثال: lock:product:1001
        var lockKey = $"lock:product:{orderDto.ProductId}";
        
        // در پایان بلاک قفل به صورت اتوماتیک آزاد می‌شود.
        await using var lockHandle = await _lockServiceWithHeartBeat.AcquireAsync(lockKey);

        // اگر قفل در اختیار درخواست دیگری باشد  
        if (lockHandle == null)
        {
            // قرار دادن سفارش در صف Redis  
            // تا بعداً توسط Worker پردازش شود.  
            await db.ListRightPushAsync("order-queue", JsonSerializer.Serialize(orderDto));

            _logger.LogInformation(
                "Order queued because lock for ProductId {ProductId} was unavailable.",
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

            var stockKey = $"product:stock:{orderDto.ProductId}";

            // مقدار stock باید از سرویس Inventory خوانده شود ولی اینجا برای تست مقدار stock را در Redis ذخیره میکنیم
            await db.StringSetAsync(
                            stockKey,
                            100,
                            TimeSpan.FromSeconds(120),
                            When.NotExists
                            );

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
                currentStock,
                TimeSpan.FromSeconds(120)
                );

            Address address = new Address("Iran", "Tehran", "Tehran", "street", "123456789");
            var order = new LockFlow.OrderService.Domain.Order.AggregateRoot.Order(address);
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
                order.Id,
                Message = ("order { OrderId} processed.", order.Id)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing order.");
            return StatusCode(500, "Internal server error.");
        }
    }
}
