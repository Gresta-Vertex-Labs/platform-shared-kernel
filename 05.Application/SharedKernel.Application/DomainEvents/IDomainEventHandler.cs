using SharedKernel.Domain.Events;

namespace SharedKernel.Application.DomainEvents;

/// <summary>
/// Handles a single domain event of type <typeparamref name="TDomainEvent"/>.
/// </summary>
/// <typeparam name="TDomainEvent">The concrete domain event type.</typeparam>
/// <remarks>
/// <typeparamref name="TDomainEvent"/> is the raw domain event (<c>03.Domain</c>). The native
/// <c>DomainEventDispatcher</c> (<c>SharedKernel.Application.Pipeline</c>) resolves every handler
/// registered for the event's concrete type through DI and runs them one after another; no mediator
/// is involved. <c>AddSharedKernelMediatR(...)</c> registers the handlers it finds in the scanned
/// assemblies, and <c>AddDomainEventHandler&lt;TDomainEvent, THandler&gt;()</c> registers one by hand. If
/// a handler needs to cross the service boundary, it must inject
/// <c>SharedKernel.Messaging.Abstractions.IEventPublisher</c> (<c>07.Messaging</c>) and publish an
/// integration event — this package has no reference to <c>07.Messaging</c> itself.
/// </remarks>
public interface IDomainEventHandler<in TDomainEvent>
    where TDomainEvent : IDomainEvent
{
    /// <summary>Handles the supplied domain event.</summary>
    /// <param name="domainEvent">The raw domain event instance.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    Task Handle(TDomainEvent domainEvent, CancellationToken cancellationToken);
}
