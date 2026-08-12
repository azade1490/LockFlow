namespace LockFlow.OrderService.Domain.Common;
public abstract class DomainEvent : IDomainEvent
{
    protected DomainEvent()
    {
        OccurredOnUtc = DateTime.UtcNow;
    }

    public DateTime OccurredOnUtc { get; }
}
