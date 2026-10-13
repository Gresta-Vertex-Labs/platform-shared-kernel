using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Contracts.Events;
using SharedKernel.Execution.Context;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.HeaderPropagation;
using SharedKernel.Messaging.Abstractions.Options;
using SharedKernel.Messaging.MassTransit.Diagnostics;
using SharedKernel.Messaging.MassTransit.Internal;
using SharedKernel.Messaging.MassTransit.MessageBus;
using SharedKernel.Messaging.MassTransit.Transports;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Primitives.Telemetry;

// Alias to disambiguate from MassTransit.PublishContext
using MessagingPublishContext = SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext;

namespace SharedKernel.Messaging.MassTransit.EventPublisher;

/// <summary>
/// MassTransit implementation of <see cref="IEventPublisher"/>.
/// Wraps integration events in <see cref="EventEnvelope{TEvent}"/> — constructed exclusively via
/// <see cref="EventEnvelope.Wrap{TEvent}"/>, never a raw object initializer (P-340/WO-054) —
/// and publishes via MassTransit. Starts a <c>messaging publish_event</c> producer span and
/// counts every attempt on <see cref="MessagingDiagnostics.PublishCounter"/> with its outcome. Both carry a
/// <c>messaging.event_type</c> tag set to the event's <see cref="IntegrationEventAttribute"/> name, the same value
/// as the envelope's CloudEvents <c>type</c>.
/// </summary>
internal sealed class MassTransitEventPublisher : IEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly MessagingOptions _messagingOptions;
    private readonly IReadOnlyList<IMessageHeaderPropagator> _propagators;
    private readonly MessagingTransport? _transport;
    private readonly ILogger<MassTransitEventPublisher> _logger;

    public MassTransitEventPublisher(
        IPublishEndpoint publishEndpoint,
        IOptions<MessagingOptions> messagingOptions,
        IEnumerable<IMessageHeaderPropagator> propagators,
        ILogger<MassTransitEventPublisher> logger,
        MessagingTransport? transport = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _publishEndpoint = publishEndpoint;
        _messagingOptions = messagingOptions.Value;
        _propagators = [.. propagators];
        _transport = transport;
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

        // The span covers validation as well as the transport call, so a contract violation is a recorded failure
        // rather than an invisible one. Independent of the EventEnvelope CorrelationId field: its trace and span ids
        // come from the ambient Activity.Current chain.
        using var activity = MessagingDiagnostics.ActivitySource.StartActivity("messaging publish_event", ActivityKind.Producer);

        // The event's declared wire name, identical to the envelope's CloudEvents "type".
        string eventTypeName;
        try
        {
            eventTypeName = IntegrationEventDescriptor.For<TEvent>().Name;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // A missing or malformed [IntegrationEvent] attribute is a contract defect in the event type, reported as
            // a Result so a caller can surface it without catching 04.Contracts' internal exception types. The wire
            // name is unknown, so the CLR name stands in as the type tag.
            var clrName = typeof(TEvent).Name;
            var violation = MassTransitErrors.ContractViolation(clrName);
            activity?.SetTag(MessagingTagKeys.MessageType, clrName);
            var violationOutcome = activity.RecordFailure(violation);
            MessagingLog.DispatchFailed(_logger, clrName, "publish", violation.Code, ex);
            MessagingDiagnostics.PublishCounter.Add(1, MessagingDiagnostics.OutcomeTags(MessagingTagKeys.MessageType, clrName, violationOutcome));
            return violation;
        }

        activity?.SetTag(MessagingTagKeys.EventType, eventTypeName);

        // Resolve CorrelationId: explicit override > a propagated X-Correlation-Id header > the ambient
        // caller's correlation id > a new one. Never the Activity's trace id, which a consumer's own trace
        // replaces: the correlation id must survive the hop unchanged (defect 4, P-566).
        string correlationId;
        if (ctx.CorrelationId.HasValue)
            correlationId = ctx.CorrelationId.Value.ToString("D");
        else if (ctx.Headers.TryGetValue(WellKnownHeaders.CorrelationId, out var propagated) && CorrelationIds.IsValid(propagated))
            correlationId = propagated;
        else
            correlationId = CorrelationIds.Current(RequestContextScope.Current) ?? CorrelationIds.New();

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
            // Result failures rather than exceptions. Only the parameter name selects the reason; the
            // exception's text never reaches the error.
            var invalid = MassTransitErrors.InvalidMessage(typeof(TEvent).Name, ex);
            var invalidOutcome = activity.RecordFailure(invalid);
            MessagingLog.DispatchFailed(_logger, eventTypeName, "publish", invalid.Code, ex);
            MessagingDiagnostics.PublishCounter.Add(1, MessagingDiagnostics.OutcomeTags(MessagingTagKeys.EventType, eventTypeName, invalidOutcome));
            return invalid;
        }

        // Parsed, not assumed: a caller-supplied correlation id need not be a GUID.
        Guid? transportCorrelationId = Guid.TryParse(correlationId, out var parsed) ? parsed : null;

        var outcome = WellKnownOutcomes.Success;
        try
        {
            // The callback always runs: it carries the tenant header and the transport correlation id on the
            // ordinary publish.
            await _publishEndpoint.Publish(
                envelope,
                pipe => PublishContextPipe.Apply(pipe, ctx, transportCorrelationId, correlationId, _transport),
                ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (OperationCanceledException ex)
        {
            outcome = activity.RecordException(ex);
            throw;
        }
        catch (Exception ex)
        {
            if (MessagingExceptionClassifier.TryClassify(ex, eventTypeName, "publish") is { } error)
            {
                activity.AddExceptionEvents(ex);
                outcome = activity.RecordFailure(error);
                MessagingLog.DispatchFailed(_logger, eventTypeName, "publish", error.Code, ex);
                return error;
            }

            outcome = activity.RecordException(ex);
            throw;
        }
        finally
        {
            MessagingDiagnostics.PublishCounter.Add(1, MessagingDiagnostics.OutcomeTags(MessagingTagKeys.EventType, eventTypeName, outcome));
        }
    }
}
