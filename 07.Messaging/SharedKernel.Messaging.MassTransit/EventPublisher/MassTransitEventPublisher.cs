using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.Options;
using SharedKernel.Contracts.Events;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.HeaderPropagation;
using SharedKernel.Messaging.Abstractions.Options;
using SharedKernel.Messaging.MassTransit.Diagnostics;
using SharedKernel.Messaging.Abstractions.Errors;
using SharedKernel.Messaging.MassTransit.Internal;
using SharedKernel.Messaging.MassTransit.MessageBus;
using SharedKernel.Primitives.Results;

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
    private readonly IReadOnlyList<IMessageHeaderPropagator> _propagators;

    public MassTransitEventPublisher(
        IPublishEndpoint publishEndpoint,
        IOptions<MessagingOptions> messagingOptions,
        IEnumerable<IMessageHeaderPropagator> propagators)
    {
        _publishEndpoint = publishEndpoint;
        _messagingOptions = messagingOptions.Value;
        _propagators = [.. propagators];
    }

    /// <inheritdoc />
    public Task<Result> PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken ct)
        where TEvent : class, IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        // HP-04: Run propagators first with no explicit configure callback.
        return PublishEnvelopeAsync(integrationEvent, BuildContext(configure: null), ct);
    }

    /// <inheritdoc />
    public Task<Result> PublishAsync<TEvent>(TEvent integrationEvent, Action<MessagingPublishContext> configure, CancellationToken ct)
        where TEvent : class, IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        ArgumentNullException.ThrowIfNull(configure);

        // HP-04: Propagators run first; explicit configure callback runs after (explicit wins on same key).
        return PublishEnvelopeAsync(integrationEvent, BuildContext(configure), ct);
    }

    /// Builds a <see cref="MessagingPublishContext"/> by running all registered propagators first,
    /// then applying the optional explicit configure callback (which wins on key conflicts).
    private MessagingPublishContext BuildContext(Action<MessagingPublishContext>? configure)
    {
        var ctx = new MessagingPublishContext();

        // Propagators run first — their values can be overridden by the explicit callback.
        for (var i = 0; i < _propagators.Count; i++)
            _propagators[i].Propagate(ctx);

        // Explicit callback runs last — its values overwrite anything set by propagators.
        configure?.Invoke(ctx);

        return ctx;
    }

    private async Task<Result> PublishEnvelopeAsync<TEvent>(TEvent integrationEvent, MessagingPublishContext ctx, CancellationToken ct)
        where TEvent : class, IIntegrationEvent
    {

        // The event's declared wire name — identical to the envelope's CloudEvents "type". Resolving it
        // before the activity starts means an event type with no valid [IntegrationEvent] attribute fails
        // fast, before any telemetry or transport work.
        string eventTypeName;
        try
        {
            eventTypeName = IntegrationEventDescriptor.For<TEvent>().Name;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // A missing or malformed [IntegrationEvent] attribute is a contract defect in the event
            // type, reported as a Result so a caller can surface it without catching 04.Contracts'
            // internal exception types (P-560).
            return MessagingErrors.ContractViolation(typeof(TEvent).Name);
        }

        // OT-04: child activity for the publish operation, disposed after the publish
        // call completes or throws. Independent of the EventEnvelope CorrelationId field —
        // this activity's TraceId/SpanId comes from the ambient Activity.Current chain.
        using var activity = MessagingDiagnostics.ActivitySource.StartActivity("EventPublisher.Publish");
        activity?.SetTag(MessagingTagKeys.EventType, eventTypeName);

        // Resolve CorrelationId: explicit override > ambient Activity.TraceId > new Guid.
        string correlationId;
        if (ctx.CorrelationId.HasValue)
            correlationId = ctx.CorrelationId.Value.ToString("D");
        else if (Activity.Current is { TraceId: var traceId })
            correlationId = traceId.ToString();
        else
            correlationId = Guid.NewGuid().ToString("D");

        // Resolve CausationId: explicit override only.
        string? causationId = ctx.CausationId.HasValue
            ? ctx.CausationId.Value.ToString("D")
            : null;

        // Resolve TenantId: explicit override only, no ambient fallback (P-340/WO-054).
        Guid? tenantId = ctx.TenantId?.Value;

        // Build the CloudEvents-compliant envelope exclusively via EventEnvelope.Wrap<TEvent>()
        // (04.Contracts's mandated factory) — never a raw object-initializer construction
        // (P-340/WO-054, fixing a confirmed prior violation of that construction rule).
        // Every optional argument is passed by name: subject, correlationId and causationId are all
        // optional strings, so a positional call would silently swap them.
        EventEnvelope<TEvent> envelope;
        try
        {
            envelope = EventEnvelope.Wrap(
                integrationEvent,
                source: _messagingOptions.ServiceName,
                subject: ctx.Subject,
                tenantId: tenantId,
                correlationId: correlationId,
                causationId: causationId);
        }
        catch (ArgumentException ex)
        {
            // Wrap rejects an event whose EventId or OccurredOn is unset, or whose TEvent is not the
            // runtime type of the argument. Both are caller mistakes the caller can fix, so they are
            // Result failures rather than exceptions (P-560).
            return MessagingErrors.InvalidMessage(typeof(TEvent).Name, ex.Message);
        }

        // Parsed, not assumed: the resolved correlation id falls back to the ambient Activity's
        // trace id, which is a 32-character hex string rather than a GUID literal.
        Guid? transportCorrelationId = Guid.TryParse(correlationId, out var parsed) ? parsed : null;

        try
        {
            // The callback always runs. P-561: the previous version skipped it entirely unless the
            // caller had supplied a header or a partition key, which silently dropped the tenant
            // header and the transport correlation id on the ordinary publish — the common case.
            await _publishEndpoint.Publish(
                envelope,
                pipe => PublishContextPipe.Apply(pipe, ctx, transportCorrelationId),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (MessagingExceptionClassifier.TryClassify(ex, eventTypeName, "publish") is { } error)
                return error;

            throw;
        }

        // P-348/WO-054: incremented only after the publish call above completes without
        // throwing — a faulted publish is never counted as published.
        MessagingDiagnostics.PublishCounter.Add(
            1, new KeyValuePair<string, object?>(MessagingTagKeys.EventType, eventTypeName));

        return Result.Success();
    }
}
