using SharedKernel.Domain.Events;

namespace SharedKernel.Contracts.Events;

/// <summary>
/// Messaging transport wrapper that carries a domain event payload alongside routing and tracing metadata.
/// </summary>
/// <typeparam name="TEvent">
/// The domain event type being wrapped. Must implement <see cref="IDomainEvent"/>.
/// </typeparam>
/// <remarks>
/// <para>
/// <see cref="EventEnvelope{TEvent}"/> is the wire format used by <c>07.Messaging</c> to publish
/// domain events across service boundaries. It attaches routing metadata (<see cref="EventType"/>,
/// <see cref="EventVersion"/>) and distributed-tracing context (<see cref="CorrelationId"/>,
/// <see cref="CausationId"/>) alongside the original domain event payload.
/// </para>
/// <para>
/// <strong>Construction:</strong> <see cref="EventEnvelope.Wrap{TEvent}"/> is the only permitted
/// construction path. Do not construct instances directly.
/// </para>
/// <para>
/// <strong>EventId identity:</strong> <see cref="EventId"/> is copied from the domain event
/// (<c>TEvent.Id</c>). It is not a new envelope-level identity. Deduplication at the transport
/// layer uses this same <see cref="EventId"/> from the payload.
/// </para>
/// </remarks>
public sealed record EventEnvelope<TEvent> where TEvent : IDomainEvent
{
    /// <summary>
    /// Gets the unique identifier of the wrapped domain event.
    /// Copied from <c>TEvent.Id</c> — not a new envelope-level identity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is <strong>not</strong> a new envelope-level identifier. It is copied verbatim from the
    /// originating domain event's <c>Id</c> property via <see cref="EventEnvelope.Wrap{TEvent}"/>.
    /// Deduplication at the transport layer (idempotent consumers) uses this same <see cref="EventId"/>.
    /// </para>
    /// <para>
    /// Preserving the domain event's identity here provides a direct trace from the wire message
    /// back to the aggregate that raised it, without an additional lookup.
    /// </para>
    /// </remarks>
    public required Guid EventId { get; init; }

    /// <summary>
    /// Gets the UTC timestamp at which the domain event occurred.
    /// Copied from <c>TEvent.OccurredOn</c>.
    /// </summary>
    /// <remarks>
    /// Sourced from the originating domain event's <c>OccurredOn</c> property at wrapping time.
    /// This reflects when the business fact occurred in the domain, not when the envelope was created
    /// or when the message was published to the broker.
    /// </remarks>
    public required DateTimeOffset OccurredOn { get; init; }

    /// <summary>
    /// Gets the event type name used for routing and deserialization.
    /// Equals <c>typeof(TEvent).Name</c> — AOT-safe; trimmer preserves <c>Type.Name</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Consumers use this value to determine which concrete type to deserialize <see cref="Payload"/>
    /// into, and message-broker routing can use it as a topic or exchange key.
    /// </para>
    /// <para>
    /// The value is derived via <c>typeof(TEvent).Name</c>, which is trimmer-safe — the .NET trimmer
    /// always preserves <c>Type.Name</c>. No reflection-based type resolution is performed.
    /// </para>
    /// </remarks>
    public required string EventType { get; init; }

    /// <summary>
    /// Gets the schema version of the event type.
    /// Sourced from <see cref="DomainEventVersionAttribute"/> via <see cref="DomainEventVersionHelper.GetVersion"/>.
    /// Defaults to <c>1</c> when the attribute is absent on <typeparamref name="TEvent"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The version is populated automatically by <see cref="EventEnvelope.Wrap{TEvent}"/> using
    /// <c>DomainEventVersionHelper.GetVersion(typeof(TEvent))</c>. Publishers do not compute version
    /// numbers manually.
    /// </para>
    /// <para>
    /// When <c>[DomainEventVersionAttribute(N)]</c> is absent on <typeparamref name="TEvent"/>, the
    /// helper returns <c>1</c> as the default. Increment the attribute value when the event shape
    /// changes in a breaking way and consumers need to distinguish schemas.
    /// </para>
    /// </remarks>
    public required int EventVersion { get; init; }

    /// <summary>
    /// Gets the distributed-trace correlation identifier propagated from ambient OTel context.
    /// <c>null</c> is a valid value when no ambient trace context is available (root events).
    /// Publishers should propagate from <c>Activity.Current?.TraceId</c> when available.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>null</c> is explicitly permitted — not every event originates within a traced request.
    /// Root events (e.g., scheduled tasks, system-initiated events) will have no ambient
    /// <c>Activity</c> and should pass <c>null</c>.
    /// </para>
    /// <para>
    /// When an ambient <c>Activity</c> exists, publishers should set this to
    /// <c>Activity.Current?.TraceId.ToString()</c> so consumers can correlate related events across
    /// service boundaries in distributed traces.
    /// </para>
    /// </remarks>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Gets the identifier of the command or event that caused this event.
    /// <c>null</c> for root events with no causal predecessor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The causation chain allows consumers to reconstruct which command or prior event triggered
    /// this event. For example, if an <c>OrderPlacedEvent</c> was caused by a <c>PlaceOrderCommand</c>,
    /// <see cref="CausationId"/> would carry the command's correlation or message identifier.
    /// </para>
    /// <para>
    /// <c>null</c> is valid for root events — those originating from user actions or scheduled
    /// processes that have no traceable causal predecessor.
    /// </para>
    /// </remarks>
    public string? CausationId { get; init; }

    /// <summary>
    /// Gets the tenant identifier this event belongs to, when the publisher is tenant-aware.
    /// <c>null</c> is valid and expected for non-tenanted or root events.
    /// </summary>
    /// <remarks>
    /// <para>
    /// (WO-052/P-331) This is envelope-level wire-format routing metadata, added so a message-bus
    /// consumer, dead-letter-queue inspector, or audit/replay tool can answer "which tenant does this
    /// event belong to" without deserializing <see cref="Payload"/>. It is populated only when the
    /// publisher explicitly supplies a value to <see cref="EventEnvelope.Wrap{TEvent}"/>; <c>null</c>
    /// is otherwise the default and is valid.
    /// </para>
    /// <para>
    /// <see cref="TenantId"/> carries no guarantee derived from <typeparamref name="TEvent"/> or
    /// <see cref="IDomainEvent"/> — <see cref="IDomainEvent"/> declares no tenant member, so this
    /// value is never inferred from <see cref="Payload"/>. It is distinct from <c>07.Messaging</c>'s
    /// <c>IMessageHeaderPropagator</c>, which is a transient, broker-adapter-specific transport header
    /// that never survives into a durably-stored outbox row or any protocol other than the one adapter
    /// that propagated it — <see cref="TenantId"/> is the durable, wire-format-level analogue. A
    /// publisher bridging <c>IMessageHeaderPropagator</c>'s tenant header into this field at
    /// composition-root/publish time is the intended integration pattern, not automatic behavior of
    /// this package.
    /// </para>
    /// <para>
    /// Do not conflate this with <c>03.Domain</c>'s <c>IHasTenant.TenantId</c> — there is no
    /// compile-time relationship between the two; this property exists so <c>04.Contracts</c> stays
    /// decoupled from any per-event tenant marker interface.
    /// </para>
    /// </remarks>
    public Guid? TenantId { get; init; }

    /// <summary>
    /// Gets the name of the service that raised this event.
    /// Set at the composition root of the publishing service.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the logical service name — for example <c>"orders-service"</c> or
    /// <c>"inventory-service"</c>. It identifies which bounded context published this message.
    /// </para>
    /// <para>
    /// The value is supplied by the caller of <see cref="EventEnvelope.Wrap{TEvent}"/> and must not
    /// be null or empty. It is typically read from application configuration at the composition root
    /// (e.g. <c>IConfiguration["ServiceName"]</c>).
    /// </para>
    /// </remarks>
    public required string SourceService { get; init; }

    /// <summary>Gets the wrapped domain event payload.</summary>
    /// <remarks>
    /// <para>
    /// This is the original domain event — unchanged from the moment <see cref="EventEnvelope.Wrap{TEvent}"/>
    /// was called. Consumers deserialize this property into the concrete <typeparamref name="TEvent"/>
    /// type to access the business data.
    /// </para>
    /// <para>
    /// The bare constraint <c>where TEvent : IDomainEvent</c> guarantees that <see cref="Payload"/>
    /// exposes only <c>Id</c> and <c>OccurredOn</c> — <c>IDomainEvent</c> has never declared an
    /// <c>AggregateId</c> member. A correlating aggregate identifier is available on
    /// <see cref="Payload"/> only when the concrete <typeparamref name="TEvent"/> additionally
    /// implements <c>03.Domain</c>'s opt-in
    /// <see cref="SharedKernel.Domain.Abstractions.IHasAggregateId{TId}"/> marker. Do not assume the
    /// member exists unconditionally — type-check instead:
    /// <code>
    /// if (envelope.Payload is IHasAggregateId&lt;OrderId&gt; correlated)
    /// {
    ///     var aggregateId = correlated.AggregateId;
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public required TEvent Payload { get; init; }
}

/// <summary>
/// Provides the static <see cref="Wrap{TEvent}"/> factory for creating <see cref="EventEnvelope{TEvent}"/> instances.
/// </summary>
public static class EventEnvelope
{
    /// <summary>
    /// Wraps a domain event in an <see cref="EventEnvelope{TEvent}"/>, populating all routing
    /// and tracing metadata automatically.
    /// </summary>
    /// <typeparam name="TEvent">The domain event type. Must implement <see cref="IDomainEvent"/>.</typeparam>
    /// <param name="domainEvent">The domain event to wrap.</param>
    /// <param name="sourceService">
    /// The name of the service publishing this event. Must not be null or empty.
    /// </param>
    /// <param name="correlationId">
    /// The distributed-trace correlation identifier from ambient OTel context.
    /// Pass <c>null</c> when no ambient context is available.
    /// </param>
    /// <param name="causationId">
    /// The identifier of the command or event that caused this domain event.
    /// Pass <c>null</c> for root events.
    /// </param>
    /// <param name="tenantId">
    /// (WO-052/P-331) The tenant this event belongs to, when the publisher is tenant-aware.
    /// Pass <c>null</c> (the default) for non-tenanted or root events. Never inferred from
    /// <paramref name="domainEvent"/> — the caller must supply it explicitly.
    /// </param>
    /// <returns>A new <see cref="EventEnvelope{TEvent}"/> with all fields populated.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="domainEvent"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="sourceService"/> is <c>null</c> or empty.
    /// </exception>
    public static EventEnvelope<TEvent> Wrap<TEvent>(
        TEvent domainEvent,
        string sourceService,
        string? correlationId = null,
        string? causationId = null,
        Guid? tenantId = null)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (string.IsNullOrEmpty(sourceService))
            throw new ArgumentException("SourceService must not be null or empty.", nameof(sourceService));

        return new EventEnvelope<TEvent>
        {
            EventId = domainEvent.Id,
            OccurredOn = domainEvent.OccurredOn,
            EventType = typeof(TEvent).Name,
            EventVersion = DomainEventVersionHelper.GetVersion(typeof(TEvent)),
            CorrelationId = correlationId,
            CausationId = causationId,
            TenantId = tenantId,
            SourceService = sourceService,
            Payload = domainEvent,
        };
    }
}
