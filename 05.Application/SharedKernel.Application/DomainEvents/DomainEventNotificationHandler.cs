using MediatR;
using SharedKernel.Domain.Events;

namespace SharedKernel.Application;

/// <summary>
/// Adapts a <see cref="DomainEventNotification{TDomainEvent}"/> to the registered
/// <see cref="IDomainEventHandler{TDomainEvent}"/>.
/// </summary>
/// <typeparam name="TDomainEvent">The concrete domain event type being adapted.</typeparam>
/// <remarks>
/// Adapter only — unwraps <c>notification.DomainEvent</c> and forwards to the registered
/// <see cref="IDomainEventHandler{TDomainEvent}"/>. Never registered directly by consuming code;
/// always registered via
/// <see cref="ApplicationServiceCollectionExtensions.AddDomainEventHandler{TDomainEvent,THandler}(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>.
/// Internal — not part of the public surface consuming services author against.
/// </remarks>
internal sealed class DomainEventNotificationHandler<TDomainEvent>(
    IDomainEventHandler<TDomainEvent> handler)
    : INotificationHandler<DomainEventNotification<TDomainEvent>>
    where TDomainEvent : IDomainEvent
{
    public Task Handle(DomainEventNotification<TDomainEvent> notification, CancellationToken cancellationToken)
        => handler.Handle(notification.DomainEvent, cancellationToken);
}
