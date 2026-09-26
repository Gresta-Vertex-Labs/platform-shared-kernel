using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.DomainEvents;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Events;

namespace SharedKernel.Application.Pipeline.DomainEvents;

/// <summary>
/// Dispatches domain events to every <see cref="IDomainEventHandler{TDomainEvent}"/> registered for
/// each event's concrete type, resolved from the current DI scope. No mediator is involved.
/// </summary>
/// <remarks>
/// <para>
/// Honors the <see cref="IDomainEventDispatcher"/> contract exactly: an empty events list is a no-op,
/// and a handler exception propagates unchanged — the remaining handlers and events are not run.
/// Dispatch is serial: the events in list order, and for each event its handlers in registration
/// order, each awaited before the next begins, because handlers commonly share the scope's
/// <c>DbContext</c>.
/// </para>
/// <para>
/// Handlers are matched on the event's exact runtime type; a handler for a base type or interface is
/// not invoked. An event with no handler is skipped.
/// </para>
/// <para>
/// <b>Runtime-type dispatch (documented exception):</b> the concrete event type is only known at
/// runtime, per element of <see cref="IReadOnlyList{IDomainEvent}"/>, so resolving
/// <c>IEnumerable&lt;IDomainEventHandler&lt;TEvent&gt;&gt;</c> needs a closed generic type built at
/// runtime. The closed invoker is created once per concrete event type and cached in a static
/// dictionary; every later dispatch of that type is an ordinary virtual call.
/// </para>
/// </remarks>
/// <param name="services">The service provider of the current DI scope.</param>
internal sealed class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, DomainEventInvoker> Invokers = new();

    /// <inheritdoc />
    public async Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);

        foreach (var domainEvent in events)
        {
            var invoker = Invokers.GetOrAdd(domainEvent.GetType(), static eventType => DomainEventInvoker.For(eventType));
            await invoker.InvokeAsync(services, domainEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    private abstract class DomainEventInvoker
    {
        public static DomainEventInvoker For(Type eventType) =>
            (DomainEventInvoker)Activator.CreateInstance(typeof(DomainEventInvoker<>).MakeGenericType(eventType))!;

        public abstract Task InvokeAsync(IServiceProvider services, IDomainEvent domainEvent, CancellationToken cancellationToken);
    }

    private sealed class DomainEventInvoker<TDomainEvent> : DomainEventInvoker
        where TDomainEvent : IDomainEvent
    {
        public override async Task InvokeAsync(IServiceProvider services, IDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            var typed = (TDomainEvent)domainEvent;
            foreach (var handler in services.GetServices<IDomainEventHandler<TDomainEvent>>())
                await handler.Handle(typed, cancellationToken).ConfigureAwait(false);
        }
    }
}
