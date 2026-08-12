namespace LockFlow.OrderService.Lock;
public sealed class LockHandle
{
    public string Key { get; init; } = default!;
    public string Value { get; init; } = default!;
    public TimeSpan Expiry { get; init; }
}
