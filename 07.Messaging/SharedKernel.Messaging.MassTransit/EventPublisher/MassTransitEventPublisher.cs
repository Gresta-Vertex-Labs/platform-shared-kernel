using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.Options;
using SharedKernel.Contracts.Events;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.HeaderPropagation;
using SharedKernel.Messaging.Abstractions.Options;
using SharedKernel.Messaging.MassTransit.Diagnostics;
using SharedKernel.Messaging.MassTransit.MessageBus;

// Alias to disambiguate from MassTransit.PublishContext
using MessagingPublishContext = SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext;

namespace SharedKernel.Messaging.MassTransit.EventPublisher;

/// <summary>
/// MassTransit implementation of <see cref="IEventPublisher"/>.
/// Wraps integration events in <see cref="EventEnvelope{TEvent}"/> — constructed exclusively via
/// <see cref="EventEnvelope.Wrap{TEvent}"/>, never a raw object initializer (P-340/WO-054) —
/// and publishes via MassTransit. Starts an <c>"EventPublisher.Publish"</c> activity and
/// increments <see cref="MessagingDiagnostics.PublishCounter"/> on successful publish
/// (P-172/P-348/WO-054). Both carry a <c>messaging.event_type</c> tag set to the event's
/// <see cref="IntegrationEventAttribute"/> name — the same value as the envelope's CloudEvents <c>type</c>.
/// </summary>
internal sealed class MassTransitEventPublisher : IEventPublisher
{
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
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken ct)
        where TEvent : class, IIntegrationEvent =>
        // HP-04: Run propagators first with no explicit configure callback.
        PublishEnvelopeAsync(integrationEvent, ctx: BuildContextFromPropagators(configure: null), ct);

    /// <inheritdoc />
    public Task PublishAsync<TEvent>(TEvent integrationEvent, Action<MessagingPublishContext> configure, CancellationToken ct)
        where TEvent : class, IIntegrationEvent
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
        where TEvent : class, IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        // The event's declared wire name — identical to the envelope's CloudEvents "type". Resolving it
        // before the activity starts means an event type with no valid [IntegrationEvent] attribute fails
        // fast, before any telemetry or transport work.
        var eventTypeName = IntegrationEventDescriptor.For<TEvent>().Name;

        // OT-04: child activity for the publish operation, disposed after the publish
        // call completes or throws. Independent of the EventEnvelope CorrelationId field —
        // this activity's TraceId/SpanId comes from the ambient Activity.Current chain.
        using var activity = MessagingDiagnostics.ActivitySource.StartActivity("EventPublisher.Publish");
        activity?.SetTag("messaging.event_type", eventTypeName);

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

        // Resolve TenantId: explicit override only, no ambient fallback (P-340/WO-054).
        Guid? tenantId = ctx?.TenantId;

        // Build the CloudEvents-compliant envelope exclusively via EventEnvelope.Wrap<TEvent>()
        // (04.Contracts's mandated factory) — never a raw object-initializer construction
        // (P-340/WO-054, fixing a confirmed prior violation of that construction rule).
        // Every optional argument is passed by name: subject, correlationId and causationId are all
        // optional strings, so a positional call would silently swap them.
        var envelope = EventEnvelope.Wrap(
            integrationEvent,
            source: _messagingOptions.ServiceName,
            subject: ctx?.Subject,
            tenantId: tenantId,
            correlationId: correlationId,
            causationId: causationId);

        // P-344/WO-054: the pipe callback must also run when only PartitionKey is set (no headers).
        if (ctx is { } publishContext && (publishContext.Headers.Count > 0 || publishContext.PartitionKey is not null))
        {
            await _publishEndpoint.Publish(envelope, pipe =>
            {
                foreach (var (key, value) in publishContext.Headers)
                    pipe.Headers.Set(key, value);

                if (Guid.TryParse(correlationId, out var corrGuid))
                    pipe.CorrelationId = corrGuid;

                // Maps to RabbitMQ routing-key affinity / Azure Service Bus session identity.
                pipe.ApplyPartitionKey(publishContext.PartitionKey);
            }, ct).ConfigureAwait(false);
        }
        else
        {
            await _publishEndpoint.Publish(envelope, ct).ConfigureAwait(false);
        }

        // P-348/WO-054: incremented only after the publish call above completes without
        // throwing — a faulted publish is never counted as published.
        MessagingDiagnostics.PublishCounter.Add(
            1, new KeyValuePair<string, object?>("messaging.event_type", eventTypeName));
    }
}
