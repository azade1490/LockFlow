namespace LockFlowWithHeartbeat.OrderService.Domain.Common;
public interface IDomainEvent
{
    DateTime OccurredOnUtc { get; }
}
