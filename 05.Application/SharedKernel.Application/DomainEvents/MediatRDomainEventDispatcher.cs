using System.Collections.Concurrent;
using System.Reflection;
using MediatR;
using Microsoft.Extensions.Options;
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
/// <para>
/// <b>Compile-time method-reference capture (WO-038, P-231):</b> the <c>BuildPublisher&lt;T&gt;</c>
/// <see cref="MethodInfo"/> is captured at type-load time by extracting it from a typed
/// <see cref="Func{T, PublishDelegate}"/> delegate instantiation — not via a nullable-suppressed
/// <c>GetMethod(...)!</c> call deferred to first use. This makes the reference verified at
/// type-load time: if <c>BuildPublisher</c> is renamed or its signature changes, the delegate
/// binding fails at startup (not at first dispatch). The technique mirrors how
/// <c>MethodBase.GetCurrentMethod()</c> provides a compile-checked reference but works for
/// generic methods where <c>GetCurrentMethod</c> is not usable.
/// </para>
/// <para>
/// <b>Parallel dispatch (WO-038, P-233):</b> when <see cref="MediatRDomainEventDispatcherOptions.ParallelDispatch"/>
/// is <see langword="true"/>, all events are dispatched concurrently via <c>Task.WhenAll</c>; any
/// exceptions are collected and rethrown as an <see cref="AggregateException"/> after all
/// dispatches complete. The serial default remains unchanged.
/// </para>
/// </remarks>
public sealed class MediatRDomainEventDispatcher : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, Delegate> PublishDelegateCache = new();

    // Compile-time method-reference capture: extract the MethodInfo from a typed delegate
    // that references BuildPublisher<T> directly. The delegate is never invoked here —
    // it is used only to obtain a verified, rename-safe MethodInfo at type-load time.
    // If BuildPublisher is renamed or its signature changes, this binding fails at startup.
    private static readonly MethodInfo BuildPublisherMethod =
        ((Func<PublishDelegate>)BuildPublisher<IDomainEvent>).Method.GetGenericMethodDefinition();

    private delegate Task PublishDelegate(IPublisher publisher, IDomainEvent domainEvent, CancellationToken ct);

    private readonly IPublisher _publisher;
    private readonly MediatRDomainEventDispatcherOptions _options;

    /// <summary>Initialises a new <see cref="MediatRDomainEventDispatcher"/>.</summary>
    /// <param name="publisher">The MediatR publisher used to dispatch wrapped notifications.</param>
    /// <param name="options">Options controlling serial vs. parallel dispatch.</param>
    public MediatRDomainEventDispatcher(
        IPublisher publisher,
        IOptions<MediatRDomainEventDispatcherOptions> options)
    {
        _publisher = publisher;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
            return;

        if (_options.ParallelDispatch)
        {
            await DispatchParallelAsync(events, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Serial dispatch (default) — each event published in list order, awaited before the next.
        foreach (var domainEvent in events)
        {
            await PublishSingle(domainEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DispatchParallelAsync(
        IReadOnlyList<IDomainEvent> events,
        CancellationToken cancellationToken)
    {
        // All events dispatched concurrently; exceptions from each are collected and wrapped in
        // an AggregateException after all dispatches complete — no event is skipped because an
        // earlier one faulted. DOCUMENTED CONSTRAINT: parallel dispatch is only valid for
        // independently-observable events with no ordering dependency between them.
        var tasks = new Task[events.Count];
        for (var i = 0; i < events.Count; i++)
        {
            // Capture loop variable for the lambda
            var domainEvent = events[i];
            tasks[i] = PublishSingle(domainEvent, cancellationToken);
        }

        // Task.WhenAll completes only after all tasks finish, ensuring no event is skipped.
        // We do NOT await the result here because `await` unwraps AggregateException to the first
        // inner exception — callers expect the full AggregateException so they can inspect every
        // handler failure. We instead wait synchronously on the already-completed task aggregate
        // and re-throw the exception as-is.
        var whenAllTask = Task.WhenAll(tasks);
        try
        {
            await whenAllTask.ConfigureAwait(false);
        }
        catch
        {
            // whenAllTask.Exception is the AggregateException wrapping all faults.
            // Throw it directly so callers receive AggregateException, not just the first inner exception.
            if (whenAllTask.Exception is not null)
                throw whenAllTask.Exception;
            throw; // fallback (should not happen when Exception != null after WhenAll fault)
        }
    }

    private Task PublishSingle(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var eventType = domainEvent.GetType();
        var publish = (PublishDelegate)PublishDelegateCache.GetOrAdd(eventType, static t =>
        {
            var method = BuildPublisherMethod.MakeGenericMethod(t);
            return (PublishDelegate)method.Invoke(null, null)!;
        });

        return publish(_publisher, domainEvent, cancellationToken);
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
