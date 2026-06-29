using System.Collections.Concurrent;
using System.Reflection;
using MediatR;
using SharedKernel.Domain;
using SharedKernel.Domain.Events;

namespace SharedKernel.Application.DomainEvents;

/// <summary>
/// MediatR-based implementation of <see cref="IDomainEventDispatcher"/>.
/// </summary>
/// <remarks>
/// <para>
/// Fulfils the forward reference recorded in <c>03.Domain/CLAUDE.md</c> ("The MediatR-based
/// implementation (<c>MediatRDomainEventDispatcher</c>) is a future <c>05.Application</c> phase").
/// Honors the <see cref="IDomainEventDispatcher"/> contract exactly: an empty events list is a
/// no-op; handler exceptions propagate unchanged (never caught/logged-and-swallowed here).
/// </para>
/// <para>
/// <b>Runtime-type dispatch (documented exception):</b> <c>events</c> is
/// <see cref="IReadOnlyList{IDomainEvent}"/> — the concrete event type is only known at runtime
/// per element. <see cref="DispatchAsync"/> resolves a cached closed-generic publish delegate from
/// a static <see cref="ConcurrentDictionary{Type, Delegate}"/>, built once per concrete event
/// <see cref="Type"/> via <see cref="MethodInfo.MakeGenericMethod"/> the first time that
/// <see cref="Type"/> is seen, then invoked directly on every subsequent dispatch of the same
/// <see cref="Type"/>. This is the same documented, justified exception to the platform-wide
/// <c>MakeGenericMethod</c>/<c>Invoke</c> prohibition already used by <c>07.Messaging</c>'s
/// <c>MassTransitEventPublisher</c> for the identical "publish-by-runtime-type through a generic
/// API" problem — not a new, ad-hoc exception. The cached delegate constructs
/// <see cref="DomainEventNotification{TDomainEvent}"/> and calls
/// <see cref="IPublisher.Publish(object, CancellationToken)"/>.
/// </para>
/// </remarks>
public sealed class MediatRDomainEventDispatcher : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, Delegate> PublishDelegateCache = new();

    private static readonly MethodInfo BuildPublisherMethod =
        typeof(MediatRDomainEventDispatcher).GetMethod(
            nameof(BuildPublisher), BindingFlags.NonPublic | BindingFlags.Static)!;

    private delegate Task PublishDelegate(IPublisher publisher, IDomainEvent domainEvent, CancellationToken ct);

    private readonly IPublisher _publisher;

    /// <summary>Initialises a new <see cref="MediatRDomainEventDispatcher"/>.</summary>
    /// <param name="publisher">The MediatR publisher used to dispatch wrapped notifications.</param>
    public MediatRDomainEventDispatcher(IPublisher publisher)
    {
        _publisher = publisher;
    }

    /// <inheritdoc />
    public async Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
            return;

        foreach (var domainEvent in events)
        {
            var eventType = domainEvent.GetType();
            var publish = (PublishDelegate)PublishDelegateCache.GetOrAdd(eventType, static t =>
            {
                var method = BuildPublisherMethod.MakeGenericMethod(t);
                return (PublishDelegate)method.Invoke(null, null)!;
            });

            await publish(_publisher, domainEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    // Called once per concrete domain event Type via MakeGenericMethod — startup-per-type cost
    // only, not a per-dispatch hot path. Returns a closed-over delegate that wraps the event in a
    // DomainEventNotification<TDomainEvent> and publishes it through MediatR.
    private static PublishDelegate BuildPublisher<TDomainEvent>()
        where TDomainEvent : IDomainEvent
    {
        return (publisher, domainEvent, ct) =>
            publisher.Publish(new DomainEventNotification<TDomainEvent>((TDomainEvent)domainEvent), ct);
    }
}
