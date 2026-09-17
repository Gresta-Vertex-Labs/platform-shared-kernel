using System.Text.Json.Serialization.Metadata;

namespace SharedKernel.Caching.Redis.PubSub;

/// <summary>
/// Publishes and subscribes to Redis Pub/Sub channels for loss-tolerant, in-the-moment signals.
/// </summary>
/// <remarks>
/// <para>
/// <b>At most once, no durability.</b> A message reaches only subscribers connected at that moment; one
/// published while a subscriber is disconnected is lost for it. Never use it for work that must happen: use
/// <c>SharedKernel.Messaging</c> for that. Cache entries need no signal of their own; the cache backplane
/// already reaches every instance.
/// </para>
/// <para>
/// <b>Subscriptions.</b> Each call to <c>SubscribeAsync</c> creates an independent subscription, so a channel
/// can have several. A subscription handles its messages one at a time, in the order Redis delivered them, and
/// ends when disposed. After a reconnect the connection restores every subscription on its own.
/// </para>
/// <para>
/// <b>Failures.</b> A handler that throws is logged and the subscription continues with the next message; a
/// typed message that cannot be deserialized is logged and skipped. Channel names are literal, never patterns.
/// </para>
/// </remarks>
public interface IRedisChannelService
{
    /// <summary>Publishes a text message.</summary>
    /// <param name="channel">The channel name.</param>
    /// <param name="message">The message.</param>
    /// <param name="ct">A token checked before the message is sent.</param>
    /// <returns>
    /// How many client connections received it; several subscriptions on one connection count once. On Redis Cluster
    /// only connections to the same node are counted.
    /// </returns>
    ValueTask<long> PublishAsync(string channel, string message, CancellationToken ct = default);

    /// <summary>Publishes a message serialized as JSON.</summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="channel">The channel name.</param>
    /// <param name="message">The message.</param>
    /// <param name="typeInfo">The JSON contract for <typeparamref name="T"/>.</param>
    /// <param name="ct">A token checked before the message is sent.</param>
    /// <returns>How many client connections received it; several subscriptions on one connection count once.</returns>
    ValueTask<long> PublishAsync<T>(string channel, T message, JsonTypeInfo<T> typeInfo, CancellationToken ct = default);

    /// <summary>Subscribes a handler to text messages on a channel.</summary>
    /// <param name="channel">The channel name.</param>
    /// <param name="handler">
    /// Called for each message. Its token is cancelled when the subscription is disposed.
    /// </param>
    /// <param name="ct">A token to cancel subscribing.</param>
    /// <returns>The subscription; dispose it to stop receiving messages.</returns>
    /// <remarks>Disposal waits for a running handler to finish, unless it is called from inside that handler.</remarks>
    ValueTask<IAsyncDisposable> SubscribeAsync(
        string channel,
        Func<string, CancellationToken, ValueTask> handler,
        CancellationToken ct = default);

    /// <summary>Subscribes a handler to JSON messages on a channel.</summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="channel">The channel name.</param>
    /// <param name="typeInfo">The JSON contract for <typeparamref name="T"/>.</param>
    /// <param name="handler">
    /// Called for each message that deserializes. Its token is cancelled when the subscription is disposed.
    /// </param>
    /// <param name="ct">A token to cancel subscribing.</param>
    /// <returns>The subscription; dispose it to stop receiving messages.</returns>
    /// <remarks>
    /// When <typeparamref name="T"/> is a reference type or a nullable value type, a message whose JSON is the
    /// literal <c>null</c> deserializes successfully and reaches the handler as <see langword="null"/> (the default of
    /// <typeparamref name="T"/>), even though the handler's parameter is declared non-nullable. Check for it when
    /// publishers may send <c>null</c>. For a non-nullable value type, <c>null</c> does not deserialize and the
    /// message is skipped.
    /// </remarks>
    ValueTask<IAsyncDisposable> SubscribeAsync<T>(
        string channel,
        JsonTypeInfo<T> typeInfo,
        Func<T, CancellationToken, ValueTask> handler,
        CancellationToken ct = default);
}
