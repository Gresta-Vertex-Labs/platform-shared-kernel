using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Messaging.Abstractions.MessageBus;

/// <summary>
/// Transport-agnostic message bus abstraction for publishing and sending messages across service boundaries.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IMessageBus"/> is a scoped service — never register or inject as singleton.
/// Singleton registration breaks MassTransit's per-consume-scope semantics.
/// </para>
/// <para>
/// Application handlers should inject <see cref="IMessageBus"/> rather than any MassTransit concrete
/// types (<c>IBus</c>, <c>IPublishEndpoint</c>, <c>ISendEndpointProvider</c>).
/// </para>
/// <para>
/// <strong>Failure contract (P-560).</strong> Every verb returns <see cref="Result"/>. Operational
/// failures the caller can reason about — an unreachable broker, an unresolvable endpoint, a
/// rejected or unserializable payload — come back as a failed <see cref="Result"/> carrying a
/// <c>messaging.*</c> code from <see cref="Errors.MessagingErrorCodes"/>, never as a transport
/// exception. Programming errors still throw: a <see langword="null"/> message is
/// <see cref="ArgumentNullException"/>, not a <see cref="Result"/>. This mirrors
/// <c>08.Storage</c>'s contract, where outage and throttling are <c>storage.unavailable</c> rather
/// than an exception.
/// </para>
/// <para>
/// A returned <see cref="Result"/> must not be discarded — <c>00.Governance</c>'s SK0030 flags a
/// Result-returning call made as a bare statement.
/// </para>
/// </remarks>
public interface IMessageBus
{
    /// <summary>
    /// Publishes a message to all consumers registered for <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The message type to publish.</typeparam>
    /// <param name="message">The message payload to publish.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A successful <see cref="Result"/> once the transport has accepted the message; otherwise a
    /// failure carrying <see cref="Errors.MessagingErrorCodes.Unavailable"/>,
    /// <see cref="Errors.MessagingErrorCodes.PublishRejected"/> or
    /// <see cref="Errors.MessagingErrorCodes.SerializationFailed"/>.
    /// </returns>
    /// <remarks>
    /// Fan-out semantics — equivalent to a topic/exchange publish.
    /// Use for integration events and broadcast notifications.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    Task<Result> PublishAsync<T>(T message, CancellationToken ct) where T : class;

    /// <summary>
    /// Publishes a message to all consumers registered for <typeparamref name="T"/>,
    /// with explicit control over transport headers and correlation metadata.
    /// </summary>
    /// <typeparam name="T">The message type to publish.</typeparam>
    /// <param name="message">The message payload to publish.</param>
    /// <param name="configure">
    /// Callback to configure <see cref="PublishContext"/> with CorrelationId, CausationId, a
    /// partition key, or custom headers. Runs after any registered header propagators, so its
    /// values win.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A successful <see cref="Result"/> once the transport has accepted the message; otherwise a
    /// failure carrying a <c>messaging.*</c> code.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="message"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    Task<Result> PublishAsync<T>(T message, Action<PublishContext> configure, CancellationToken ct) where T : class;

    /// <summary>
    /// Sends a command to the single registered endpoint for <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The command type to send.</typeparam>
    /// <param name="command">The command payload to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A successful <see cref="Result"/> once the transport has accepted the command; otherwise a
    /// failure carrying <see cref="Errors.MessagingErrorCodes.EndpointNotFound"/>,
    /// <see cref="Errors.MessagingErrorCodes.Unavailable"/> or
    /// <see cref="Errors.MessagingErrorCodes.SerializationFailed"/>.
    /// </returns>
    /// <remarks>
    /// Point-to-point semantics — equivalent to a queue send.
    /// The endpoint address is resolved by convention from the transport provider.
    /// Use for commands and work items with exactly one handler.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
    Task<Result> SendAsync<T>(T command, CancellationToken ct) where T : class;
}
