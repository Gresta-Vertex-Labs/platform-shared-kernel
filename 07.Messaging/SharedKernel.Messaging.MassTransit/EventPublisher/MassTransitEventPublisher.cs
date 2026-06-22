using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using MassTransit;
using Microsoft.Extensions.Options;
using SharedKernel.Contracts.Events;
using SharedKernel.Domain.Events;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.HeaderPropagation;
using SharedKernel.Messaging.Abstractions.Options;
using SharedKernel.Messaging.MassTransit.Diagnostics;

// Alias to disambiguate from MassTransit.PublishContext
using MessagingPublishContext = SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext;

namespace SharedKernel.Messaging.MassTransit.EventPublisher;

/// <summary>
/// MassTransit implementation of <see cref="IEventPublisher"/>.
/// Wraps integration events in <see cref="EventEnvelope{TEvent}"/> and publishes via MassTransit.
/// </summary>
internal sealed class MassTransitEventPublisher : IEventPublisher
{
    // Cache of (IPublishEndpoint, object event, string sourceService, PublishContext?, CancellationToken) → Task delegates
    // keyed by closed TEvent type. Built once per type; subsequent calls are direct delegate invocations.
    private static readonly ConcurrentDictionary<Type, PublishDelegate> _publisherCache = new();

    private delegate Task PublishDelegate(
        IPublishEndpoint publishEndpoint,
        object integrationEvent,
        string sourceService,
        MessagingPublishContext? ctx,
        CancellationToken ct);

    private static readonly MethodInfo BuildPublisherMethod =
        typeof(MassTransitEventPublisher).GetMethod(
            nameof(BuildPublisher), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly IPublishEndpoint _publishEndpoint;
    private readonly MessagingOptions _messagingOptions;
    private readonly IEnumerable<IMessageHeaderPropagator> _propagators;

    public MassTransitEventPublisher(
        IPublishEndpoint publishEndpoint,
        IOptions<MessagingOptions> messagingOptions,
        IEnumerable<IMessageHeaderPropagator> propagators)
    {
        _publishEndpoint = publishEndpoint;
        _messagingOptions = messagingOptions.Value;
        _propagators = propagators;
    }

    /// <inheritdoc />
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken ct) where TEvent : class =>
        // HP-04: Run propagators first with no explicit configure callback.
        PublishEnvelopeAsync(integrationEvent, ctx: BuildContextFromPropagators(configure: null), ct);

    /// <inheritdoc />
    public Task PublishAsync<TEvent>(TEvent integrationEvent, Action<MessagingPublishContext> configure, CancellationToken ct) where TEvent : class
    {
        // HP-04: Propagators run first; explicit configure callback runs after (explicit wins on same key).
        var ctx = BuildContextFromPropagators(configure);
        return PublishEnvelopeAsync(integrationEvent, ctx, ct);
    }

    /// Builds a <see cref="MessagingPublishContext"/> by running all registered propagators first,
    /// then applying the optional explicit configure callback (which wins on key conflicts).
    private MessagingPublishContext? BuildContextFromPropagators(Action<MessagingPublishContext>? configure)
    {
        var hasPropagators = _propagators.Any();
        var hasExplicit = configure is not null;

        if (!hasPropagators && !hasExplicit)
            return null;

        var ctx = new MessagingPublishContext();

        // Propagators run first — their values can be overridden by the explicit callback.
        foreach (var propagator in _propagators)
            propagator.Propagate(ctx);

        // Explicit callback runs last — its values overwrite anything set by propagators.
        configure?.Invoke(ctx);

        return ctx;
    }

    private async Task PublishEnvelopeAsync<TEvent>(TEvent integrationEvent, MessagingPublishContext? ctx, CancellationToken ct)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var eventType = typeof(TEvent);

        // OT-04: child activity for the publish operation, disposed after the publish
        // call completes or throws. Independent of the EventEnvelope CorrelationId field —
        // this activity's TraceId/SpanId comes from the ambient Activity.Current chain.
        using var activity = MessagingDiagnostics.ActivitySource.StartActivity("EventPublisher.Publish");
        activity?.SetTag("messaging.event_type", eventType.Name);

        if (!typeof(IDomainEvent).IsAssignableFrom(eventType))
            throw new InvalidOperationException(
                $"IEventPublisher only supports integration events that implement IDomainEvent. " +
                $"Type '{eventType.Name}' does not implement IDomainEvent. " +
                $"Use IMessageBus.PublishAsync<T> for plain message types.");

        var publisher = _publisherCache.GetOrAdd(eventType, static t =>
        {
            var method = BuildPublisherMethod.MakeGenericMethod(t);
            return (PublishDelegate)method.Invoke(null, null)!;
        });

        await publisher(_publishEndpoint, integrationEvent, _messagingOptions.ServiceName, ctx, ct).ConfigureAwait(false);
    }

    // Called once per TEvent type via MakeGenericMethod — startup cost only, not a hot path.
    // Returns a closed-over delegate that satisfies the IDomainEvent constraint.
    private static PublishDelegate BuildPublisher<TEvent>()
        where TEvent : class, IDomainEvent
    {
        return (publishEndpoint, eventObj, sourceService, ctx, ct) =>
        {
            var integrationEvent = (TEvent)eventObj;
            return PublishEnvelope(publishEndpoint, integrationEvent, sourceService, ctx, ct);
        };
    }

    private static Task PublishEnvelope<TEvent>(
        IPublishEndpoint publishEndpoint,
        TEvent integrationEvent,
        string sourceService,
        MessagingPublishContext? ctx,
        CancellationToken ct)
        where TEvent : class, IDomainEvent
    {
        // Resolve CorrelationId: explicit override > ambient Activity.TraceId > new Guid.
        string correlationId;
        if (ctx?.CorrelationId.HasValue == true)
            correlationId = ctx.CorrelationId.Value.ToString("D");
        else if (Activity.Current is { TraceId: var traceId })
            correlationId = traceId.ToString();
        else
            correlationId = Guid.NewGuid().ToString("D");

        // Resolve CausationId: explicit override only.
        string? causationId = ctx?.CausationId.HasValue == true
            ? ctx.CausationId.Value.ToString("D")
            : null;

        // Build the CloudEvents-compliant envelope.
        var envelope = new EventEnvelope<TEvent>
        {
            EventId = integrationEvent.Id,
            OccurredOn = integrationEvent.OccurredOn,
            EventType = typeof(TEvent).Name,
            EventVersion = DomainEventVersionHelper.GetVersion(typeof(TEvent)),
            CorrelationId = correlationId,
            CausationId = causationId,
            SourceService = sourceService,
            Payload = integrationEvent,
        };

        if (ctx?.Headers is { Count: > 0 } headers)
        {
            return publishEndpoint.Publish(envelope, pipe =>
            {
                foreach (var (key, value) in headers)
                    pipe.Headers.Set(key, value);

                if (Guid.TryParse(correlationId, out var corrGuid))
                    pipe.CorrelationId = corrGuid;
            }, ct);
        }

        return publishEndpoint.Publish(envelope, ct);
    }
}
