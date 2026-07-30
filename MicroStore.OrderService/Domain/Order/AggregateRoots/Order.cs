using MicroStore.OrderService.Domain.Common;
using MicroStore.OrderService.Domain.Order.Entities;
using MicroStore.OrderService.Domain.Order.Enums;
using MicroStore.OrderService.Domain.Order.ValueObjects;

namespace MicroStore.OrderService.Domain.Order.AggregateRoot;
public sealed class Order : AggregateRoot<Guid>
{
    private readonly List<OrderItem> _items = new();

    private Order() { } // EF Core

    public Order(Guid orderId,Address shippingAddress)
    {
        Id = orderId;
        Status = OrderStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
        ShippingAddress = shippingAddress;
    }
    
    public OrderStatus Status { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public Address ShippingAddress { get; private set; } = default!;

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    //public Money TotalAmount => new Money(
    //    _items.Sum(i => i.TotalPrice.Amount),
    //    _items.FirstOrDefault()?.TotalPrice.Currency ?? "IRR");

    //Sum فقط برای انواع عددی مثل int، decimal و double تعریف شده است. اما Money یک کلاس است، Sum نمی‌داند دو شیء Money را چگونه با هم جمع کند.
    public Money TotalAmount =>
_items
    .Select(i => i.TotalPrice)
    .Aggregate(Money.Zero("IRR"), (total, money) => total + money);

    public void AddItem(Guid productId, string productName, Money unitPrice, int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));

        var item = _items.FirstOrDefault(x => x.ProductId == productId);

        if (item is null)
        {
            _items.Add(new OrderItem(productId, productName, unitPrice, quantity));
        }
        else
        {
            item.IncreaseQuantity(quantity);
        }
    }

    public void Confirm()
    {
        Status = OrderStatus.Confirmed;
    }

    public void Cancel()
    {
        Status = OrderStatus.Cancelled;
    }

    public void MarkAsPaid()
    {
        Status = OrderStatus.Paid;
    }
}
