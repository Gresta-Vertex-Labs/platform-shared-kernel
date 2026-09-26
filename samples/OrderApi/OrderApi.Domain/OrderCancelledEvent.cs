using SharedKernel.Domain.Events;

namespace OrderApi.Domain;

/// <summary>Raised when an <see cref="Order"/> is cancelled.</summary>
public sealed record OrderCancelledEvent(Guid OrderId) : IDomainEvent
{
    public Guid Id { get; } = Guid.CreateVersion7();
    public DateTimeOffset OccurredOn { get; init; }
}
