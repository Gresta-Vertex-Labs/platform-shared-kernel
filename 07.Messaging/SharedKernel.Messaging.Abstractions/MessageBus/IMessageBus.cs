using SharedKernel.Messaging.Abstractions.EventPublisher;

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
/// </remarks>
public interface IMessageBus
{
    /// <summary>
    /// Publishes a message to all consumers registered for <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The message type to publish.</typeparam>
    /// <param name="message">The message payload to publish.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the message has been accepted by the transport.</returns>
    /// <remarks>
    /// Fan-out semantics — equivalent to a topic/exchange publish.
    /// Use for integration events and broadcast notifications.
    /// </remarks>
    Task PublishAsync<T>(T message, CancellationToken ct) where T : class;

    /// <summary>
    /// Publishes a message to all consumers registered for <typeparamref name="T"/>,
    /// with explicit control over transport headers and correlation metadata.
    /// </summary>
    /// <typeparam name="T">The message type to publish.</typeparam>
    /// <param name="message">The message payload to publish.</param>
    /// <param name="configure">Callback to configure <see cref="PublishContext"/> with CorrelationId, CausationId, or custom headers.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the message has been accepted by the transport.</returns>
    Task PublishAsync<T>(T message, Action<PublishContext> configure, CancellationToken ct) where T : class;

    /// <summary>
    /// Sends a command to the single registered endpoint for <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The command type to send.</typeparam>
    /// <param name="command">The command payload to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the command has been accepted by the transport.</returns>
    /// <remarks>
    /// Point-to-point semantics — equivalent to a queue send.
    /// The endpoint address is resolved by convention from the transport provider.
    /// Use for commands and work items with exactly one handler.
    /// </remarks>
    Task SendAsync<T>(T command, CancellationToken ct) where T : class;

    /// <summary>
    /// Sends a request and waits for a response using the request/response pattern over the message bus.
    /// </summary>
    /// <typeparam name="TRequest">The request message type.</typeparam>
    /// <typeparam name="TResponse">The expected response message type.</typeparam>
    /// <param name="request">The request payload.</param>
    /// <param name="ct">Cancellation token. Must be timeout-bound — never pass <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that resolves to the response message.</returns>
    /// <remarks>
    /// <para>
    /// Uses a private temporary reply queue under the hood.
    /// </para>
    /// <para>
    /// <strong>Caution:</strong> This pattern adds latency and creates tight temporal coupling between services.
    /// Prefer event-driven fire-and-forget patterns wherever possible.
    /// Always pass a timeout-bound <see cref="CancellationToken"/>; passing <see cref="CancellationToken.None"/>
    /// will hang indefinitely if the responder is unavailable or slow.
    /// </para>
    /// </remarks>
    Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct)
        where TRequest : class
        where TResponse : class;
}
