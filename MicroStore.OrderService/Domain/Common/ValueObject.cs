namespace MicroStore.OrderService.Domain.Common;
// record انتخاب بهتری است، چون ویژگی‌های اصلی Value Object را به‌صورت پیش‌فرض دارد:
// مقایسه بر اساس مقدار (Value Equality)
// پیاده‌سازی Equals
// پیاده‌سازی GetHashCode
// عملگرهای == و !=
// پشتیبانی مناسب از Immutable بودن
// اما چند استثنا وجود دارد:
// اگر می‌خواهید تمام Value Objectها متدهای مشترکی داشته باشند (مثلاً Validate() یا Clone()).
// اگر می‌خواهید رفتار مقایسه را به شکلی متفاوت از رفتار پیش‌فرض record پیاده‌سازی کنید.
public abstract class ValueObject
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public override bool Equals(object? obj)
    {
        if (obj is null || obj.GetType() != GetType())
            return false;

        var other = (ValueObject)obj;

        return GetEqualityComponents()
            .SequenceEqual(other.GetEqualityComponents());
    }

    public override int GetHashCode()
    {
        return GetEqualityComponents()
            .Aggregate(1, (current, obj) =>
            {
                unchecked
                {
                    return current * 23 + (obj?.GetHashCode() ?? 0);
                }
            });
    }

    public static bool operator ==(ValueObject? left, ValueObject? right)
        => Equals(left, right);

    public static bool operator !=(ValueObject? left, ValueObject? right)
        => !Equals(left, right);
}
