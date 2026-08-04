using System.Collections.Concurrent;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.HeaderPropagation;
using SharedKernel.Messaging.Abstractions.MessageBus;

namespace SharedKernel.Testing.Messaging;

/// <summary>
/// In-memory test double for <see cref="IMessageBus"/>. Records every <c>PublishAsync</c>/<c>SendAsync</c>/
/// <c>RequestAsync</c> call — including its captured <see cref="PublishContext"/> — for later assertion.
/// </summary>
/// <remarks>
/// <para>
/// Records every call — message type plus instance plus captured <see cref="PublishContext"/> —
/// into a thread-safe list even when no assertion is ever made. The <c>ShouldHave*</c> assertion
/// helpers are read-only queries over that list and never mutate it.
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
/// <para>
/// <strong>Header propagation (P-352/WO-054):</strong> every dispatch verb (<c>PublishAsync</c>
/// both overloads, <c>SendAsync</c>, <c>RequestAsync</c>) builds a fresh <see cref="PublishContext"/>,
/// runs every constructor-supplied <see cref="IMessageHeaderPropagator"/> in enumeration order, then
/// — where an explicit <c>configure</c> callback exists — invokes it last. Explicit callback values
/// win over propagated values on any key both set. This precedence mirrors the real, shipped
/// <c>MassTransitMessageBus.BuildContextFromPropagators</c> helper exactly (<c>SendAsync</c>/
/// <c>RequestAsync</c> have no <c>configure</c> parameter on the real interface, but propagators still
/// run for them — an internally-built, uncustomizable-by-the-caller context — matching production's
/// target contract that propagator application is symmetric across all dispatch verbs).
/// </para>
/// <para>
/// <strong>Reference-capture design:</strong> the <c>ShouldHave*Context</c> accessors below return a
/// reference to the actual <see cref="PublishContext"/> instance built for that call — never a copy of
/// named scalar fields. Any property later added to <see cref="PublishContext"/> becomes visible through
/// these same accessors automatically, with no further changes to this type.
/// </para>
/// </remarks>
public sealed class InMemoryMessageBus : IMessageBus
{
    private readonly IReadOnlyList<IMessageHeaderPropagator> _propagators;
    private readonly ConcurrentQueue<(Type Type, object Message, bool IsSend, PublishContext Context)> _recorded = new();
    private readonly ConcurrentQueue<(Type RequestType, Type ResponseType, PublishContext Context)> _requestContexts = new();
    private readonly ConcurrentDictionary<(Type Request, Type Response), Delegate> _responseHandlers = new();

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryMessageBus"/>.
    /// </summary>
    /// <param name="propagators">
    /// Optional header propagators, run in enumeration order against a fresh
    /// <see cref="PublishContext"/> before every dispatch call. Defaults to none — the existing
    /// parameterless <c>new InMemoryMessageBus()</c> construction pattern remains valid unchanged.
    /// </param>
    /// <remarks>
    /// <strong>Singleton-fake-vs-scoped-propagator caveat:</strong> <see cref="IMessageHeaderPropagator"/>
    /// is documented as a scoped service in production, but this fake is registered as a singleton
    /// (see <see cref="MessagingServiceCollectionExtensions.AddInMemoryMessageBus"/>). Resolving a
    /// production-shaped scoped propagator into this constructor while DI scope validation is enabled
    /// throws a captive-dependency <see cref="InvalidOperationException"/> — register propagator test
    /// doubles as Singleton or Transient, never Scoped, when composing the DI container for this fake.
    /// </remarks>
    public InMemoryMessageBus(IEnumerable<IMessageHeaderPropagator>? propagators = null)
    {
        _propagators = propagators?.ToArray() ?? [];
    }

    /// <inheritdoc />
    public Task PublishAsync<T>(T message, CancellationToken ct) where T : class
    {
        ArgumentNullException.ThrowIfNull(message);
        var context = BuildContext(configure: null);
        _recorded.Enqueue((typeof(T), message, IsSend: false, context));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PublishAsync<T>(T message, Action<PublishContext> configure, CancellationToken ct) where T : class
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(configure);
        var context = BuildContext(configure);
        _recorded.Enqueue((typeof(T), message, IsSend: false, context));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendAsync<T>(T command, CancellationToken ct) where T : class
    {
        ArgumentNullException.ThrowIfNull(command);
        var context = BuildContext(configure: null);
        _recorded.Enqueue((typeof(T), command, IsSend: true, context));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct)
        where TRequest : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = BuildContext(configure: null);
        _requestContexts.Enqueue((typeof(TRequest), typeof(TResponse), context));

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

    /// <summary>
    /// Returns the <see cref="PublishContext"/> captured for the first recorded <c>PublishAsync</c>
    /// call of type <typeparamref name="T"/> — a reference to the real instance that was built for
    /// that call, not a copy.
    /// </summary>
    /// <typeparam name="T">The expected message type.</typeparam>
    /// <returns>The captured context.</returns>
    /// <exception cref="InvalidOperationException">No matching message was published.</exception>
    public PublishContext ShouldHavePublishedContext<T>() where T : class =>
        FindFirstContext<T>(isSend: false, "published");

    /// <summary>
    /// Returns the <see cref="PublishContext"/> captured for the first recorded <c>SendAsync</c>
    /// call of type <typeparamref name="T"/> — a reference to the real instance that was built for
    /// that call, not a copy.
    /// </summary>
    /// <typeparam name="T">The expected message type.</typeparam>
    /// <returns>The captured context.</returns>
    /// <exception cref="InvalidOperationException">No matching message was sent.</exception>
    public PublishContext ShouldHaveSentContext<T>() where T : class =>
        FindFirstContext<T>(isSend: true, "sent");

    /// <summary>
    /// Returns the <see cref="PublishContext"/> captured for the first recorded
    /// <c>RequestAsync</c> call for the given <typeparamref name="TRequest"/>/<typeparamref name="TResponse"/>
    /// pair — a reference to the real instance that was built for that call, not a copy.
    /// </summary>
    /// <typeparam name="TRequest">The expected request message type.</typeparam>
    /// <typeparam name="TResponse">The expected response message type.</typeparam>
    /// <returns>The captured context.</returns>
    /// <exception cref="InvalidOperationException">No matching request was made.</exception>
    public PublishContext ShouldHaveRequestedContext<TRequest, TResponse>()
        where TRequest : class
        where TResponse : class
    {
        var match = _requestContexts.FirstOrDefault(r =>
            r.RequestType == typeof(TRequest) && r.ResponseType == typeof(TResponse));
        if (match.Context is null)
        {
            throw new InvalidOperationException(
                $"Expected a requested call for request type '{typeof(TRequest).Name}' and " +
                $"response type '{typeof(TResponse).Name}' but none was found.");
        }

        return match.Context;
    }

    /// Builds a fresh <see cref="PublishContext"/>, running every constructor-supplied propagator
    /// first (in enumeration order), then the explicit <paramref name="configure"/> callback last —
    /// explicit values win over propagated values on any key both set.
    private PublishContext BuildContext(Action<PublishContext>? configure)
    {
        var context = new PublishContext();
        foreach (var propagator in _propagators)
        {
            propagator.Propagate(context);
        }

        configure?.Invoke(context);
        return context;
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

    private PublishContext FindFirstContext<T>(bool isSend, string verb) where T : class
    {
        var match = _recorded.FirstOrDefault(r => r.IsSend == isSend && r.Type == typeof(T));
        if (match.Message is null)
        {
            throw new InvalidOperationException(
                $"Expected a {verb} message of type '{typeof(T).Name}' but none was found.");
        }

        return match.Context;
    }
}
