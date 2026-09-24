using SharedKernel.Domain.Events;

namespace SharedKernel.Application;

/// <summary>
/// Handles a single domain event of type <typeparamref name="TDomainEvent"/>.
/// </summary>
/// <typeparam name="TDomainEvent">The concrete domain event type.</typeparam>
/// <remarks>
/// <typeparamref name="TDomainEvent"/> is the raw domain event (<c>03.Domain</c>) — not a MediatR
/// notification. Consuming services implement this interface directly; they never implement
/// MediatR's <c>INotificationHandler&lt;&gt;</c> for domain events. If a handler needs to cross the
/// service boundary, it must inject <c>SharedKernel.Messaging.Abstractions.IEventPublisher</c>
/// (<c>07.Messaging</c>) and publish an integration event — this package has no reference to
/// <c>07.Messaging</c> itself; that wiring happens in the consuming service's own handler
/// implementation.
/// </remarks>
public interface IDomainEventHandler<in TDomainEvent>
    where TDomainEvent : IDomainEvent
{
    /// <summary>Handles the supplied domain event.</summary>
    /// <param name="domainEvent">The raw domain event instance.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    Task Handle(TDomainEvent domainEvent, CancellationToken cancellationToken);
}
