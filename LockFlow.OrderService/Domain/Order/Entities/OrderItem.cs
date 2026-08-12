
using LockFlow.OrderService.Domain.Common;
using LockFlow.OrderService.Domain.Order.ValueObjects;

namespace LockFlow.OrderService.Domain.Order.Entities;
public sealed class OrderItem:Entity<Guid>
{
    private OrderItem() { } // EF Core

    public OrderItem(
        Guid productId,
        string productName,
        Money unitPrice,
        int quantity)
    {
        Id = Guid.NewGuid();
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    public Guid ProductId { get; private set; }

    public string ProductName { get; private set; } = default!;

    public Money UnitPrice { get; private set; } = default!;

    public int Quantity { get; private set; }

    public Money TotalPrice =>
        new Money(UnitPrice.Amount * Quantity, UnitPrice.Currency);

    public void IncreaseQuantity(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));

        Quantity += quantity;
    }
}
