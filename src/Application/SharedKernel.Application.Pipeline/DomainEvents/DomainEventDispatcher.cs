using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.DomainEvents;
using SharedKernel.Application.Pipeline.Diagnostics;
using SharedKernel.Application.Pipeline.Tracing;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Events;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Application.Pipeline.DomainEvents;

/// <summary>
/// Dispatches domain events to every <see cref="IDomainEventHandler{TDomainEvent}"/> registered for
/// each event's concrete type, resolved from the current DI scope. No mediator is involved.
/// </summary>
/// <remarks>
/// <para>
/// Honors the <see cref="IDomainEventDispatcher"/> contract exactly: an empty events list is a no-op,
/// and a handler exception propagates unchanged (the same instance) — the remaining handlers and events are not run.
/// Dispatch is serial: the events in list order, and for each event its handlers in registration
/// order, each awaited before the next begins, because handlers commonly share the scope's
/// <c>DbContext</c>.
/// </para>
/// <para>
/// Handlers are matched on the event's exact runtime type; a handler for a base type or interface is
/// not invoked. An event with no handler is skipped.
/// </para>
/// <para>
/// <b>Telemetry.</b> A non-empty dispatch is one <c>application dispatch_events</c> span (tag
/// <c>application.event_count</c>) with one child <c>application handle_event</c> span per event type that has at
/// least one handler (tag <c>application.event_type</c>). A handler fault is logged once (event type and handler type,
/// never the event payload), recorded on both spans through the shared recorder, and rethrown unchanged. Cancellation
/// is neither an error status nor an error log.
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
/// <param name="logger">The logger for handler failures.</param>
internal sealed class DomainEventDispatcher(IServiceProvider services, ILogger<DomainEventDispatcher> logger)
    : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, DomainEventInvoker> Invokers = new();

    /// <inheritdoc />
    public async Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0)
            return;

        using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(
            ApplicationSpanNames.DispatchEvents,
            ActivityKind.Internal);
        activity?.SetTag(ApplicationTagKeys.EventCount, events.Count);

        try
        {
            foreach (var domainEvent in events)
            {
                var invoker = Invokers.GetOrAdd(domainEvent.GetType(), static eventType => DomainEventInvoker.For(eventType));
                await invoker.InvokeAsync(services, domainEvent, logger, cancellationToken).ConfigureAwait(false);
            }

            activity?.SetTag(WellKnownTagKeys.Outcome, WellKnownOutcomes.Success);
        }
        catch (Exception exception)
        {
            activity?.SetTag(WellKnownTagKeys.Outcome, activity.RecordException(exception));
            throw;
        }
    }

    private abstract class DomainEventInvoker
    {
        public static DomainEventInvoker For(Type eventType) =>
            (DomainEventInvoker)Activator.CreateInstance(typeof(DomainEventInvoker<>).MakeGenericType(eventType))!;

        public abstract Task InvokeAsync(
            IServiceProvider services,
            IDomainEvent domainEvent,
            ILogger logger,
            CancellationToken cancellationToken);
    }

    private sealed class DomainEventInvoker<TDomainEvent> : DomainEventInvoker
        where TDomainEvent : IDomainEvent
    {
        public override async Task InvokeAsync(
            IServiceProvider services,
            IDomainEvent domainEvent,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var typed = (TDomainEvent)domainEvent;
            IDomainEventHandler<TDomainEvent>[] handlers = [.. services.GetServices<IDomainEventHandler<TDomainEvent>>()];
            if (handlers.Length == 0)
                return;

            var eventType = typeof(TDomainEvent).FullName ?? typeof(TDomainEvent).Name;
            using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(
                ApplicationSpanNames.HandleEvent,
                ActivityKind.Internal);
            activity?.SetTag(ApplicationTagKeys.EventType, eventType);

            foreach (var handler in handlers)
            {
                try
                {
                    await handler.Handle(typed, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    var handlerType = handler.GetType().FullName ?? handler.GetType().Name;
                    activity?.SetTag(ApplicationTagKeys.HandlerType, handlerType);
                    activity?.SetTag(WellKnownTagKeys.Outcome, activity.RecordException(exception));

                    // Cancellation is not a failure, so it is not logged as one.
                    if (exception is not OperationCanceledException)
                    {
                        PipelineLog.DomainEventHandlerFailed(
                            logger,
                            exception is IHasError ? LogLevel.Warning : LogLevel.Error,
                            eventType,
                            handlerType,
                            exception);
                    }

                    throw;
                }
            }

            activity?.SetTag(WellKnownTagKeys.Outcome, WellKnownOutcomes.Success);
        }
    }
}
