namespace SharedKernel.Messaging.Abstractions.EventPublisher;

/// <summary>
/// Everything about one dispatch that is not the message itself: who it correlates to, which
/// tenant it is for, what caused it, which partition it belongs on, and any headers of your own.
/// </summary>
/// <remarks>
/// <para>
/// Reached through the <see cref="Action{T}"/> overload of
/// <see cref="SharedKernel.Messaging.Abstractions.MessageBus.IMessageBus.PublishAsync{T}(T, Action{PublishContext}, CancellationToken)"/>
/// and <see cref="IEventPublisher.PublishAsync{TEvent}(TEvent, Action{PublishContext}, CancellationToken)"/>.
/// The bus owns the instance; the callback only configures it.
/// </para>
/// <para>
/// <strong>Precedence: propagators first, your callback last.</strong> Every registered
/// <see cref="HeaderPropagation.IMessageHeaderPropagator"/> runs before the callback, so an
/// explicit value here always wins over the ambient one. That is how a background job publishes on
/// behalf of a tenant it is not itself scoped to, and it means you never have to disable a
/// propagator to override it once.
/// </para>
/// <para>
/// Most publishes need none of this. Correlation and tenant arrive on their own when
/// <c>WithAmbientCorrelationPropagation()</c> and <c>WithInboundRequestContext()</c> are enabled —
/// reach for the callback only for the values only this call site knows.
/// </para>
/// </remarks>
public sealed class PublishContext
{
    private readonly Dictionary<string, string> _headers = [];

    /// <summary>
    /// Gets the explicit correlation identifier to embed in the published envelope.
    /// <c>null</c> instructs the publisher to auto-populate from <c>Activity.Current?.TraceId</c>.
    /// </summary>
    public Guid? CorrelationId { get; private set; }

    /// <summary>
    /// Gets the causation identifier of the command or event that triggered this publish.
    /// <c>null</c> means no causation chain is attached.
    /// </summary>
    public Guid? CausationId { get; private set; }

    /// <summary>
    /// Gets the tenant identifier the published event belongs to.
    /// <c>null</c> means the tenant is omitted from the envelope, exactly like an unset
    /// <see cref="CorrelationId"/> or <see cref="CausationId"/>.
    /// </summary>
    /// <remarks>
    /// Flows into <c>EventEnvelope&lt;TEvent&gt;.TenantId</c> (<c>04.Contracts</c>, P-331) on the
    /// <c>IEventPublisher</c> path. On the <c>IMessageBus</c> path, which has no envelope, it is
    /// written as the <c>X-Tenant-Id</c> transport header
    /// (<c>01.Core</c>'s <see cref="SharedKernel.Primitives.Propagation.WellKnownHeaders.TenantId"/>)
    /// — the same name every other domain propagates tenant identity under. Setting it used to be a
    /// silent no-op on that path; P-560 made it real.
    /// </remarks>
    public Guid? TenantId { get; private set; }

    /// <summary>
    /// Gets the partition/affinity key used to derive ordered-delivery routing for the outgoing
    /// message. <c>null</c> means no ordered-delivery affinity is requested.
    /// </summary>
    /// <remarks>
    /// Maps to RabbitMQ routing-key affinity or Azure Service Bus session identity depending on the
    /// configured transport — see "Ordered delivery via partition key" in
    /// <c>07.Messaging/CLAUDE.md</c> (P-344/WO-054). Ordering is guaranteed only among messages
    /// sharing the same <see cref="PartitionKey"/> and consumed by a single active consumer instance
    /// on that endpoint.
    /// </remarks>
    public string? PartitionKey { get; private set; }

    /// <summary>
    /// Gets the resource the published event is about, such as <c>order/42</c>.
    /// <c>null</c> means the subject is omitted from the envelope.
    /// </summary>
    /// <remarks>
    /// Flows into the CloudEvents <c>subject</c> attribute (<c>EventEnvelope&lt;TEvent&gt;.Subject</c>,
    /// <c>04.Contracts</c>) so brokers and subscribers can filter on it without reading the event data.
    /// <para>
    /// <strong>Envelope-only.</strong> Unlike <see cref="TenantId"/>, this is a CloudEvents attribute
    /// with no meaning outside an envelope, so it is ignored on the <c>IMessageBus</c> path — that
    /// path publishes a bare message, and giving <c>subject</c> a transport header there would invent
    /// a wire convention no other domain reads. Use <c>IEventPublisher</c> when you need it (P-560).
    /// </para>
    /// </remarks>
    public string? Subject { get; private set; }

    /// <summary>
    /// Gets the custom transport headers to attach to the outgoing message.
    /// Keys are non-null, non-empty. Duplicate keys overwrite the earlier value.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers => _headers;

    /// <summary>
    /// Sets the correlation identifier, overriding the ambient <c>Activity.Current?.TraceId</c>.
    /// </summary>
    /// <param name="correlationId">The correlation identifier to embed.</param>
    /// <returns>This <see cref="PublishContext"/> instance for fluent chaining.</returns>
    public PublishContext WithCorrelationId(Guid correlationId)
    {
        CorrelationId = correlationId;
        return this;
    }

    /// <summary>
    /// Sets the causation identifier for the outgoing message.
    /// </summary>
    /// <param name="causationId">The causation identifier to embed.</param>
    /// <returns>This <see cref="PublishContext"/> instance for fluent chaining.</returns>
    public PublishContext WithCausationId(Guid causationId)
    {
        CausationId = causationId;
        return this;
    }

    /// <summary>
    /// Sets the tenant identifier for the outgoing integration event envelope.
    /// </summary>
    /// <param name="tenantId">The tenant identifier the published event belongs to.</param>
    /// <returns>This <see cref="PublishContext"/> instance for fluent chaining.</returns>
    public PublishContext WithTenantId(Guid tenantId)
    {
        TenantId = tenantId;
        return this;
    }

    /// <summary>
    /// Sets the partition/affinity key used to derive ordered-delivery routing for the outgoing message.
    /// </summary>
    /// <param name="partitionKey">
    /// The partition key, e.g. an aggregate instance identifier. Must not be null or empty.
    /// </param>
    /// <returns>This <see cref="PublishContext"/> instance for fluent chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="partitionKey"/> is null or empty.</exception>
    public PublishContext WithPartitionKey(string partitionKey)
    {
        if (string.IsNullOrEmpty(partitionKey))
            throw new ArgumentException("Partition key must not be null or empty.", nameof(partitionKey));

        PartitionKey = partitionKey;
        return this;
    }

    /// <summary>
    /// Sets the CloudEvents subject for the outgoing integration event envelope.
    /// </summary>
    /// <param name="subject">
    /// The resource the event is about, e.g. <c>order/42</c>. Must not be null, empty or whitespace.
    /// </param>
    /// <returns>This <see cref="PublishContext"/> instance for fluent chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="subject"/> is null, empty or whitespace.</exception>
    public PublishContext WithSubject(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("Subject must not be null, empty or whitespace.", nameof(subject));

        Subject = subject;
        return this;
    }

    /// <summary>
    /// Adds or overwrites a custom transport header on the outgoing message.
    /// </summary>
    /// <param name="key">The header key. Must not be null or empty.</param>
    /// <param name="value">The header value.</param>
    /// <returns>This <see cref="PublishContext"/> instance for fluent chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is null or empty.</exception>
    public PublishContext WithHeader(string key, string value)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Header key must not be null or empty.", nameof(key));

        _headers[key] = value;
        return this;
    }
}
