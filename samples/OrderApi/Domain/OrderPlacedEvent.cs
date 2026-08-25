using SharedKernel.Domain.Events;

namespace OrderApi.Domain;

/// <summary>Raised when an <see cref="Order"/> is placed.</summary>
public sealed record OrderPlacedEvent(Guid OrderId, string Customer, decimal Total, string Currency)
    : IDomainEvent
{
    public Guid Id { get; } = Guid.CreateVersion7();
    public DateTimeOffset OccurredOn { get; init; }
}
