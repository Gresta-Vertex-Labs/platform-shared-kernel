using System.Collections.Concurrent;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.HeaderPropagation;

namespace SharedKernel.Testing.Messaging;

/// <summary>
/// In-memory test double for <see cref="IEventPublisher"/>. Records every published integration
/// event — including its captured <see cref="PublishContext"/> — for later assertion.
/// </summary>
/// <remarks>
/// <para>
/// Does not wrap events in <c>EventEnvelope&lt;TEvent&gt;</c> — that is a
/// <c>MassTransitEventPublisher</c>-specific transport concern. This double records the raw
/// <typeparamref name="TEvent"/> instances only. Thread-safe under concurrent publish.
/// </para>
/// <para>
/// <strong>Header propagation (P-352/WO-054):</strong> both <c>PublishAsync</c> overloads build a
/// fresh <see cref="PublishContext"/>, run every constructor-supplied
/// <see cref="IMessageHeaderPropagator"/> in enumeration order, then — where an explicit
/// <c>configure</c> callback exists — invoke it last. Explicit callback values win over propagated
/// values on any key both set, mirroring <see cref="InMemoryMessageBus"/>'s identical precedence.
/// </para>
/// <para>
/// <strong>Reference-capture design:</strong> <see cref="ShouldHavePublishedContext{TEvent}"/> returns
/// a reference to the actual <see cref="PublishContext"/> instance built for that call — never a copy
/// of named scalar fields — so any property later added to <see cref="PublishContext"/> becomes
/// visible through the same accessor automatically, with no further changes to this type.
/// </para>
/// </remarks>
public sealed class InMemoryEventPublisher : IEventPublisher
{
    private readonly IReadOnlyList<IMessageHeaderPropagator> _propagators;
    private readonly ConcurrentQueue<(object Event, PublishContext Context)> _published = new();

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryEventPublisher"/>.
    /// </summary>
    /// <param name="propagators">
    /// Optional header propagators, run in enumeration order against a fresh
    /// <see cref="PublishContext"/> before every <c>PublishAsync</c> call. Defaults to none — the
    /// existing parameterless <c>new InMemoryEventPublisher()</c> construction pattern remains valid
    /// unchanged.
    /// </param>
    /// <remarks>
    /// <strong>Singleton-fake-vs-scoped-propagator caveat:</strong> <see cref="IMessageHeaderPropagator"/>
    /// is documented as a scoped service in production, but this fake is registered as a singleton
    /// (see <see cref="MessagingServiceCollectionExtensions.AddInMemoryEventPublisher"/>). Resolving a
    /// production-shaped scoped propagator into this constructor while DI scope validation is enabled
    /// throws a captive-dependency <see cref="InvalidOperationException"/> — register propagator test
    /// doubles as Singleton or Transient, never Scoped, when composing the DI container for this fake.
    /// </remarks>
    public InMemoryEventPublisher(IEnumerable<IMessageHeaderPropagator>? propagators = null)
    {
        _propagators = propagators?.ToArray() ?? [];
    }

    /// <summary>Gets every integration event published so far, in publish order.</summary>
    public IReadOnlyList<object> Published => _published.Select(p => p.Event).ToArray();

    /// <inheritdoc />
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken ct) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var context = BuildContext(configure: null);
        _published.Enqueue((integrationEvent, context));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PublishAsync<TEvent>(TEvent integrationEvent, Action<PublishContext> configure, CancellationToken ct)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        ArgumentNullException.ThrowIfNull(configure);

        var context = BuildContext(configure);
        _published.Enqueue((integrationEvent, context));
        return Task.CompletedTask;
    }

    /// <summary>Returns every published event of type <typeparamref name="TEvent"/>, in publish order.</summary>
    /// <typeparam name="TEvent">The integration event type to filter by.</typeparam>
    /// <returns>The matching published events.</returns>
    public IReadOnlyList<TEvent> PublishedOf<TEvent>() where TEvent : class =>
        _published.Select(p => p.Event).OfType<TEvent>().ToList();

    /// <summary>
    /// Returns the first recorded published event of type <typeparamref name="TEvent"/>.
    /// </summary>
    /// <typeparam name="TEvent">The expected event type.</typeparam>
    /// <returns>The matched event.</returns>
    /// <exception cref="InvalidOperationException">No matching event was published.</exception>
    public TEvent ShouldHavePublished<TEvent>() where TEvent : class
    {
        var match = _published.Select(p => p.Event).OfType<TEvent>().FirstOrDefault();
        if (match is null)
        {
            throw new InvalidOperationException(
                $"Expected a published event of type '{typeof(TEvent).Name}' but none was found.");
        }

        return match;
    }

    /// <summary>
    /// Asserts that exactly one event of type <typeparamref name="TEvent"/> was published, and
    /// returns it.
    /// </summary>
    /// <typeparam name="TEvent">The expected event type.</typeparam>
    /// <returns>The single matched event.</returns>
    /// <exception cref="InvalidOperationException">Zero or more than one matching event was published.</exception>
    public TEvent ShouldHavePublishedOnce<TEvent>() where TEvent : class
    {
        var matches = _published.Select(p => p.Event).OfType<TEvent>().ToList();
        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one published event of type '{typeof(TEvent).Name}' but found {matches.Count}.");
        }

        return matches[0];
    }

    /// <summary>Asserts that no event of type <typeparamref name="TEvent"/> was published.</summary>
    /// <typeparam name="TEvent">The event type that must not have been published.</typeparam>
    /// <exception cref="InvalidOperationException">A matching event was published.</exception>
    public void ShouldNotHavePublished<TEvent>() where TEvent : class
    {
        var count = _published.Select(p => p.Event).OfType<TEvent>().Count();
        if (count > 0)
        {
            throw new InvalidOperationException(
                $"Expected no published events of type '{typeof(TEvent).Name}' but found {count}.");
        }
    }

    /// <summary>
    /// Returns the <see cref="PublishContext"/> captured for the first recorded published event of
    /// type <typeparamref name="TEvent"/> — a reference to the real instance that was built for that
    /// call, not a copy.
    /// </summary>
    /// <typeparam name="TEvent">The expected event type.</typeparam>
    /// <returns>The captured context.</returns>
    /// <exception cref="InvalidOperationException">No matching event was published.</exception>
    public PublishContext ShouldHavePublishedContext<TEvent>() where TEvent : class
    {
        var match = _published.FirstOrDefault(p => p.Event is TEvent);
        if (match.Event is null)
        {
            throw new InvalidOperationException(
                $"Expected a published event of type '{typeof(TEvent).Name}' but none was found.");
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
}
