using System.Collections.Concurrent;
using System.Linq.Expressions;
using MediatR;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Events;

namespace SharedKernel.Application.DomainEvents;

/// <summary>
/// MediatR-based implementation of <see cref="IDomainEventDispatcher"/>.
/// </summary>
/// <remarks>
/// <para>
/// Honors the <see cref="IDomainEventDispatcher"/> contract exactly: an empty events list is a
/// no-op; handler exceptions propagate unchanged (never caught or swallowed here). Dispatch is
/// always serial — each event is wrapped and published in list order, awaited before the next
/// begins.
/// </para>
/// <para>
/// <b>Runtime-type dispatch (documented exception):</b> <c>events</c> is
/// <see cref="IReadOnlyList{IDomainEvent}"/> — the concrete event type is only known at runtime per
/// element. Wrapping a domain event in a closed <see cref="DomainEventNotification{TDomainEvent}"/>
/// therefore requires constructing a closed generic type at runtime. This is resolved via a
/// per-concrete-event-type cached factory — an <see cref="Expression"/> tree calling the closed
/// <see cref="DomainEventNotification{TDomainEvent}"/> constructor directly, compiled once via
/// <see cref="LambdaExpression.Compile()"/> — stored in a static
/// <see cref="ConcurrentDictionary{TKey, TValue}"/> keyed by the event's <see cref="Type"/>, so every
/// dispatch after the first for a given concrete event type reuses the compiled delegate with no
/// further reflection. This is the same class of documented exception already approved for
/// <c>07.Messaging</c>'s <c>MassTransitEventPublisher</c> for the identical
/// "construct/publish-by-runtime-type through a generic API" problem — compiling instead of using
/// <see cref="Activator.CreateInstance(Type, object?[])"/> per call trades a one-time build cost for
/// a delegate invocation on every subsequent dispatch of that event type.
/// </para>
/// </remarks>
public sealed class MediatRDomainEventDispatcher(IPublisher publisher) : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, Func<IDomainEvent, INotification>> NotificationFactories =
        new();

    /// <inheritdoc />
    public async Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
            return;

        foreach (var domainEvent in events)
        {
            var notification = BuildNotification(domainEvent);
            await publisher.Publish(notification, cancellationToken).ConfigureAwait(false);
        }
    }

    private static INotification BuildNotification(IDomainEvent domainEvent)
    {
        var factory = NotificationFactories.GetOrAdd(domainEvent.GetType(), static eventType => CompileFactory(eventType));
        return factory(domainEvent);
    }

    /// <summary>
    /// Builds and compiles <c>domainEvent => new DomainEventNotification&lt;TEventType&gt;((TEventType)domainEvent)</c>
    /// for the closed <paramref name="eventType"/>, invoked exactly once per concrete event type.
    /// </summary>
    private static Func<IDomainEvent, INotification> CompileFactory(Type eventType)
    {
        var notificationType = typeof(DomainEventNotification<>).MakeGenericType(eventType);
        var constructor = notificationType.GetConstructor([eventType])!;

        var parameter = Expression.Parameter(typeof(IDomainEvent), "domainEvent");
        var typedArgument = Expression.Convert(parameter, eventType);
        var construct = Expression.New(constructor, typedArgument);
        var asNotification = Expression.Convert(construct, typeof(INotification));

        return Expression.Lambda<Func<IDomainEvent, INotification>>(asNotification, parameter).Compile();
    }
}
