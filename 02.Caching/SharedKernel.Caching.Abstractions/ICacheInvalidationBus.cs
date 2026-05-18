namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// Broadcasts cache invalidation signals to peer services over an ephemeral transport.
/// </summary>
/// <remarks>
/// <para>
/// <b>No delivery guarantees.</b> Invalidation messages are transmitted over Redis Pub/Sub,
/// which provides at-most-once delivery. If a subscriber service is offline when a message is
/// published, it will not receive the invalidation and must rely on TTL expiry to eventually
/// serve fresh data. This is by design: cache invalidation is a best-effort optimisation, not a
/// correctness guarantee.
/// </para>
/// <para>
/// <b>Durable delivery belongs in <c>07.Messaging</c>.</b> If your scenario requires
/// guaranteed, ordered, or transactional delivery of invalidation events, use
/// <c>SharedKernel.Messaging</c> (MassTransit) instead of this interface.
/// </para>
/// <para>
/// The default implementation (<c>RedisCacheInvalidationBus</c> in
/// <c>SharedKernel.Caching.Redis</c>) publishes to two channels:
/// a service-targeted channel (<c>sharedkernel:cache:invalidation:{service-name}</c>) and
/// a broadcast channel (<c>sharedkernel:cache:invalidation:broadcast</c>).
/// </para>
/// </remarks>
public interface ICacheInvalidationBus
{
    /// <summary>
    /// Publishes an invalidation signal for the specified cache <paramref name="keys"/>.
    /// Receivers will call <c>ICacheService.RemoveAsync</c> for each key.
    /// </summary>
    /// <param name="keys">One or more cache keys to invalidate. Must not be empty.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask PublishKeyInvalidationAsync(string[] keys, CancellationToken ct = default);

    /// <summary>
    /// Publishes an invalidation signal for all entries carrying any of the specified
    /// <paramref name="tags"/>. Receivers will call <c>ICacheService.RemoveByTagAsync</c>
    /// for each tag.
    /// </summary>
    /// <param name="tags">One or more cache tags to invalidate. Must not be empty.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask PublishTagInvalidationAsync(string[] tags, CancellationToken ct = default);

    /// <summary>
    /// Publishes a broadcast invalidation signal requesting all receivers flush their L1 caches.
    /// </summary>
    /// <remarks>
    /// This is a break-glass operation. Receivers log a structured warning and rely on TTL
    /// expiry rather than attempting to enumerate all keys. Use only when a full-cache
    /// invalidation is operationally justified (e.g., a bulk data migration).
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    ValueTask PublishBroadcastInvalidationAsync(CancellationToken ct = default);

    /// <summary>
    /// Publishes a fully-constructed <see cref="CacheInvalidationMessage"/> over the bus.
    /// Use when you need precise control over all message fields, including
    /// <see cref="CacheInvalidationMessage.CorrelationId"/> for distributed tracing continuity.
    /// </summary>
    /// <param name="message">The invalidation message to publish. Must not be <see langword="null"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask PublishInvalidationAsync(CacheInvalidationMessage message, CancellationToken ct = default);
}
