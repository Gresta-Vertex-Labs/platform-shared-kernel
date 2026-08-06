namespace SharedKernel.Messaging.Abstractions.EventPublisher;

/// <summary>
/// Mutable builder for configuring per-publish metadata such as correlation identifiers
/// and custom transport headers. Passed as an <see cref="Action{T}"/> callback to
/// <see cref="IEventPublisher"/> and <see cref="SharedKernel.Messaging.Abstractions.MessageBus.IMessageBus"/> publish overloads.
/// </summary>
/// <remarks>
/// <para>
/// Callers configure the instance via the callback; the implementation owns the lifetime.
/// </para>
/// <para>
/// Header keys must be non-null, non-empty strings. Duplicate keys overwrite silently.
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
    /// Flows into <c>EventEnvelope&lt;TEvent&gt;.TenantId</c> (<c>04.Contracts</c>, P-331) via the
    /// <c>IEventPublisher</c> path only — <c>IMessageBus</c> has no envelope to carry it, so setting
    /// <see cref="TenantId"/> on a plain <c>IMessageBus.PublishAsync</c>/<c>SendAsync</c> call is a
    /// no-op today (P-340/WO-054).
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
