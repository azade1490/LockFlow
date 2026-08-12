using LockFlow.OrderService.Domain.Common;

namespace LockFlow.OrderService.Domain.Order.ValueObjects;
public sealed class Money : ValueObject
{
    //مقدار پول
    public decimal Amount { get; }

    //واحد پول مثل USD برای دلار و IRR برای ریال ایران
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    //حتی اگر سفارش هیچ آیتمی نداشته باشد، خروجی به‌صورت زیر خواهد بود: new Money(0, "IRR") و هیچ خطایی رخ نمی‌دهد.
    public static Money Zero(string currency)
        => new Money(0, currency);

    //عملگر + را هم داخل Money پیاده‌سازی کنی تا فقط پول‌هایی با ارز یکسان قابل جمع باشند
    public static Money operator +(Money left, Money right)
    {
        if (left.Currency != right.Currency)
            throw new InvalidOperationException("Cannot add money with different currencies.");

        return new Money(left.Amount + right.Amount, left.Currency);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
}
