using MediatR;
using SharedKernel.Domain.Events;

namespace SharedKernel.Application.DomainEvents;

/// <summary>
/// Wraps a raw <see cref="IDomainEvent"/> so it can be published through MediatR's
/// <see cref="IPublisher"/>.
/// </summary>
/// <typeparam name="TDomainEvent">The concrete domain event type being wrapped.</typeparam>
/// <param name="DomainEvent">The wrapped domain event instance.</param>
/// <remarks>
/// This record is the seam that absorbs the MediatR dependency on behalf of the domain layer.
/// <see cref="IDomainEvent"/> itself (<c>03.Domain</c>) cannot implement MediatR's
/// <see cref="INotification"/> — <c>03.Domain</c> has zero NuGet dependencies and must stay that
/// way.
/// </remarks>
public sealed record DomainEventNotification<TDomainEvent>(TDomainEvent DomainEvent) : INotification
    where TDomainEvent : IDomainEvent;
