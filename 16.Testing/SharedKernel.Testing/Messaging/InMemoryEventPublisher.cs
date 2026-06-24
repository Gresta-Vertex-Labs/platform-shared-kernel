using System.Collections.Concurrent;
using SharedKernel.Messaging.Abstractions.EventPublisher;

namespace SharedKernel.Testing.Messaging;

/// <summary>
/// In-memory test double for <see cref="IEventPublisher"/>. Records every published integration
/// event for later assertion.
/// </summary>
/// <remarks>
/// Does not wrap events in <c>EventEnvelope&lt;TEvent&gt;</c> — that is a
/// <c>MassTransitEventPublisher</c>-specific transport concern. This double records the raw
/// <typeparamref name="TEvent"/> instances only. Thread-safe under concurrent publish.
/// </remarks>
public sealed class InMemoryEventPublisher : IEventPublisher
{
    private readonly ConcurrentQueue<object> _published = new();

    /// <summary>Gets every integration event published so far, in publish order.</summary>
    public IReadOnlyList<object> Published => _published.ToArray();

    /// <inheritdoc />
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken ct) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        _published.Enqueue(integrationEvent);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PublishAsync<TEvent>(TEvent integrationEvent, Action<PublishContext> configure, CancellationToken ct)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        ArgumentNullException.ThrowIfNull(configure);

        configure(new PublishContext());
        _published.Enqueue(integrationEvent);
        return Task.CompletedTask;
    }

    /// <summary>Returns every published event of type <typeparamref name="TEvent"/>, in publish order.</summary>
    /// <typeparam name="TEvent">The integration event type to filter by.</typeparam>
    /// <returns>The matching published events.</returns>
    public IReadOnlyList<TEvent> PublishedOf<TEvent>() where TEvent : class =>
        _published.OfType<TEvent>().ToList();

    /// <summary>
    /// Returns the first recorded published event of type <typeparamref name="TEvent"/>.
    /// </summary>
    /// <typeparam name="TEvent">The expected event type.</typeparam>
    /// <returns>The matched event.</returns>
    /// <exception cref="InvalidOperationException">No matching event was published.</exception>
    public TEvent ShouldHavePublished<TEvent>() where TEvent : class
    {
        var match = _published.OfType<TEvent>().FirstOrDefault();
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
        var matches = _published.OfType<TEvent>().ToList();
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
        var count = _published.OfType<TEvent>().Count();
        if (count > 0)
        {
            throw new InvalidOperationException(
                $"Expected no published events of type '{typeof(TEvent).Name}' but found {count}.");
        }
    }
}
