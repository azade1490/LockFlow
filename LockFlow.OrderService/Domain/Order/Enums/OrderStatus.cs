namespace LockFlow.OrderService.Domain.Order.Enums;
public enum OrderStatus
{
    Pending = 1,
    Confirmed = 2,
    Paid = 3,
    Shipped = 4,
    Delivered = 5,
    Cancelled = 6
}
