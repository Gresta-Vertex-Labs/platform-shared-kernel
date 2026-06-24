using System.Collections.Concurrent;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.MessageBus;

namespace SharedKernel.Testing.Messaging;

/// <summary>
/// In-memory test double for <see cref="IMessageBus"/>. Records every <c>PublishAsync</c>/<c>SendAsync</c>
/// call for later assertion.
/// </summary>
/// <remarks>
/// <para>
/// Records every call — message type plus instance — into a thread-safe list even when no
/// assertion is ever made. The <c>ShouldHave*</c> assertion helpers are read-only queries over
/// that list and never mutate it.
/// </para>
/// <para>
/// <see cref="RequestAsync{TRequest, TResponse}"/> is configurable via
/// <see cref="SetResponseHandler{TRequest, TResponse}"/>; when no handler is registered for the
/// requested type pair, it throws a descriptive <see cref="InvalidOperationException"/>.
/// </para>
/// <para>
/// <see cref="ExecuteRoutingSlipAsync"/> throws <see cref="NotSupportedException"/> by design —
/// true routing-slip orchestration requires a real broker round-trip; tests needing that fidelity
/// use MassTransit's own <c>ITestHarness</c> (see <see cref="TestHarnessFactory"/>) instead.
/// </para>
/// </remarks>
public sealed class InMemoryMessageBus : IMessageBus
{
    private readonly ConcurrentQueue<(Type Type, object Message, bool IsSend)> _recorded = new();
    private readonly ConcurrentDictionary<(Type Request, Type Response), Delegate> _responseHandlers = new();

    /// <inheritdoc />
    public Task PublishAsync<T>(T message, CancellationToken ct) where T : class
    {
        ArgumentNullException.ThrowIfNull(message);
        _recorded.Enqueue((typeof(T), message, IsSend: false));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PublishAsync<T>(T message, Action<PublishContext> configure, CancellationToken ct) where T : class
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(configure);
        configure(new PublishContext());
        _recorded.Enqueue((typeof(T), message, IsSend: false));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendAsync<T>(T command, CancellationToken ct) where T : class
    {
        ArgumentNullException.ThrowIfNull(command);
        _recorded.Enqueue((typeof(T), command, IsSend: true));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct)
        where TRequest : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_responseHandlers.TryGetValue((typeof(TRequest), typeof(TResponse)), out var handler))
        {
            throw new InvalidOperationException(
                $"No response handler registered for request type '{typeof(TRequest).Name}' and " +
                $"response type '{typeof(TResponse).Name}'. Call SetResponseHandler<{typeof(TRequest).Name}, " +
                $"{typeof(TResponse).Name}>(...) before calling RequestAsync.");
        }

        var typedHandler = (Func<TRequest, TResponse>)handler;
        return Task.FromResult(typedHandler(request));
    }

    /// <inheritdoc />
    public Task ExecuteRoutingSlipAsync(object routingSlip, CancellationToken ct) =>
        throw new NotSupportedException(
            "InMemoryMessageBus does not support routing slip orchestration — true routing-slip " +
            "execution requires a real broker round-trip. Use MassTransit's ITestHarness (see " +
            "TestHarnessFactory) for tests that need this fidelity.");

    /// <summary>
    /// Registers (or replaces) the handler <see cref="RequestAsync{TRequest, TResponse}"/>
    /// delegates to for the given <typeparamref name="TRequest"/>/<typeparamref name="TResponse"/> pair.
    /// </summary>
    /// <typeparam name="TRequest">The request message type.</typeparam>
    /// <typeparam name="TResponse">The response message type.</typeparam>
    /// <param name="handler">The synchronous handler producing a response from a request.</param>
    public void SetResponseHandler<TRequest, TResponse>(Func<TRequest, TResponse> handler)
        where TRequest : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(handler);
        _responseHandlers[(typeof(TRequest), typeof(TResponse))] = handler;
    }

    /// <summary>
    /// Returns the first recorded <c>PublishAsync</c> message of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The expected message type.</typeparam>
    /// <returns>The matched message.</returns>
    /// <exception cref="InvalidOperationException">No matching message was published.</exception>
    public T ShouldHavePublished<T>() where T : class =>
        FindFirst<T>(isSend: false, "published");

    /// <summary>
    /// Returns the first recorded <c>SendAsync</c> message of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The expected message type.</typeparam>
    /// <returns>The matched message.</returns>
    /// <exception cref="InvalidOperationException">No matching message was sent.</exception>
    public T ShouldHaveSent<T>() where T : class =>
        FindFirst<T>(isSend: true, "sent");

    /// <summary>
    /// Asserts that exactly one message of type <typeparamref name="T"/> was published, and returns it.
    /// </summary>
    /// <typeparam name="T">The expected message type.</typeparam>
    /// <returns>The single matched message.</returns>
    /// <exception cref="InvalidOperationException">Zero or more than one matching message was published.</exception>
    public T ShouldHavePublishedOnce<T>() where T : class
    {
        var matches = _recorded.Where(r => !r.IsSend && r.Type == typeof(T)).ToList();
        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one published message of type '{typeof(T).Name}' but found {matches.Count}.");
        }

        return (T)matches[0].Message;
    }

    /// <summary>Asserts that no message of type <typeparamref name="T"/> was published.</summary>
    /// <typeparam name="T">The message type that must not have been published.</typeparam>
    /// <exception cref="InvalidOperationException">A matching message was published.</exception>
    public void ShouldNotHavePublished<T>() where T : class
    {
        var count = _recorded.Count(r => !r.IsSend && r.Type == typeof(T));
        if (count > 0)
        {
            throw new InvalidOperationException(
                $"Expected no published messages of type '{typeof(T).Name}' but found {count}.");
        }
    }

    private T FindFirst<T>(bool isSend, string verb) where T : class
    {
        var match = _recorded.FirstOrDefault(r => r.IsSend == isSend && r.Type == typeof(T));
        if (match.Message is null)
        {
            throw new InvalidOperationException(
                $"Expected a {verb} message of type '{typeof(T).Name}' but none was found.");
        }

        return (T)match.Message;
    }
}
