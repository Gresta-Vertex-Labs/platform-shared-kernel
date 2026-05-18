namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// Provides ephemeral, non-durable Redis Pub/Sub fanout for cache-adjacent signaling.
/// </summary>
/// <remarks>
/// <para>
/// This service is scoped to <b>cache-adjacent ephemeral signaling only</b>: cache invalidation
/// signals, lightweight broadcast notifications, and other transient coordination patterns where
/// message loss is tolerable. It must <b>never</b> be used for durable, ordered, or
/// guaranteed-delivery messaging — that is the responsibility of <c>07.Messaging</c>
/// (<c>SharedKernel.Messaging</c> / MassTransit).
/// </para>
/// <para>
/// Because this service is ephemeral, non-durable: if a subscriber is offline when a message
/// is published, the message is silently dropped. Receivers that require at-least-once delivery
/// must use <c>07.Messaging</c> instead.
/// </para>
/// <para>
/// All handler exceptions are caught and logged internally — they are never propagated to the
/// Redis subscriber thread.
/// </para>
/// </remarks>
public interface IRedisChannelService
{
    /// <summary>
    /// Publishes <paramref name="message"/> to the specified Redis <paramref name="channel"/>.
    /// </summary>
    /// <param name="channel">
    /// The literal channel name. Must not be null or whitespace.
    /// </param>
    /// <param name="message">The string payload to publish.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask PublishAsync(string channel, string message, CancellationToken ct = default);

    /// <summary>
    /// Subscribes <paramref name="handler"/> to the specified Redis <paramref name="channel"/>.
    /// </summary>
    /// <remarks>
    /// The handler is invoked asynchronously on each received message. If the handler throws,
    /// the exception is caught, logged, and processing continues — the subscriber thread is
    /// never exposed to handler failures.
    /// </remarks>
    /// <param name="channel">The literal channel name to subscribe to.</param>
    /// <param name="handler">
    /// Async callback invoked with the raw message string on each publication.
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask SubscribeAsync(string channel, Func<string, ValueTask> handler, CancellationToken ct = default);

    /// <summary>
    /// Unsubscribes from the specified Redis <paramref name="channel"/> and removes any
    /// registered handler. No-ops if not currently subscribed.
    /// </summary>
    /// <param name="channel">The literal channel name to unsubscribe from.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask UnsubscribeAsync(string channel, CancellationToken ct = default);
}
