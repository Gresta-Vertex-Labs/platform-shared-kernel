using SharedKernel.Domain.Events;

namespace SharedKernel.Testing.Domain;

/// <summary>
/// Framework-agnostic assertion helpers over <see cref="IReadOnlyCollection{T}"/> of
/// <see cref="IDomainEvent"/> — typically <c>AggregateRoot&lt;TId&gt;.DomainEvents</c>.
/// </summary>
/// <remarks>
/// All assertions throw <see cref="InvalidOperationException"/> with a descriptive message on
/// failure. This package has zero dependency on xUnit, NUnit, or FluentAssertions.
/// </remarks>
public static class DomainEventAssertions
{
    /// <summary>
    /// Asserts that <paramref name="events"/> contains at least one event of type
    /// <typeparamref name="T"/> and returns the first matching instance.
    /// </summary>
    /// <typeparam name="T">The expected domain event type.</typeparam>
    /// <param name="events">The collection of raised domain events.</param>
    /// <returns>The first matching event of type <typeparamref name="T"/>.</returns>
    /// <exception cref="InvalidOperationException">No event of type <typeparamref name="T"/> was found.</exception>
    public static T ContainsEventOfType<T>(this IReadOnlyCollection<IDomainEvent> events)
        where T : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(events);

        var match = events.OfType<T>().FirstOrDefault();
        if (match is null)
        {
            throw new InvalidOperationException(
                $"Expected an event of type '{typeof(T).Name}' but none was found. " +
                $"Raised events: [{string.Join(", ", events.Select(e => e.GetType().Name))}].");
        }

        return match;
    }

    /// <summary>
    /// Asserts that <paramref name="events"/> contains exactly <paramref name="count"/> events
    /// of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The expected domain event type.</typeparam>
    /// <param name="events">The collection of raised domain events.</param>
    /// <param name="count">The expected number of matching events.</param>
    /// <exception cref="InvalidOperationException">The actual count does not equal <paramref name="count"/>.</exception>
    public static void ContainsExactly<T>(this IReadOnlyCollection<IDomainEvent> events, int count)
        where T : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(events);

        var actual = events.OfType<T>().Count();
        if (actual != count)
        {
            throw new InvalidOperationException(
                $"Expected exactly {count} event(s) of type '{typeof(T).Name}' but found {actual}.");
        }
    }

    /// <summary>Asserts that <paramref name="events"/> is empty.</summary>
    /// <param name="events">The collection of raised domain events.</param>
    /// <exception cref="InvalidOperationException"><paramref name="events"/> is non-empty.</exception>
    public static void HasNoEvents(this IReadOnlyCollection<IDomainEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count > 0)
        {
            throw new InvalidOperationException(
                $"Expected no domain events but found {events.Count}: " +
                $"[{string.Join(", ", events.Select(e => e.GetType().Name))}].");
        }
    }

    /// <summary>
    /// Asserts that <paramref name="events"/> contains no events of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The domain event type that must not be present.</typeparam>
    /// <param name="events">The collection of raised domain events.</param>
    /// <exception cref="InvalidOperationException">An event of type <typeparamref name="T"/> was found.</exception>
    public static void HasNoEventsOfType<T>(this IReadOnlyCollection<IDomainEvent> events)
        where T : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(events);

        var actual = events.OfType<T>().Count();
        if (actual > 0)
        {
            throw new InvalidOperationException(
                $"Expected no events of type '{typeof(T).Name}' but found {actual}.");
        }
    }

    /// <summary>
    /// Asserts that <paramref name="events"/> contains an event of type <typeparamref name="T"/>
    /// whose declared <see cref="DomainEventVersionHelper"/> version equals
    /// <paramref name="version"/>, and returns the matching instance.
    /// </summary>
    /// <typeparam name="T">The expected domain event type.</typeparam>
    /// <param name="events">The collection of raised domain events.</param>
    /// <param name="version">The expected schema version (see <see cref="DomainEventVersionAttribute"/>).</param>
    /// <returns>The matching event of type <typeparamref name="T"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// No event of type <typeparamref name="T"/> was found, or its declared version does not equal
    /// <paramref name="version"/>.
    /// </exception>
    public static T ContainsEventWithVersion<T>(this IReadOnlyCollection<IDomainEvent> events, int version)
        where T : IDomainEvent
    {
        var match = events.ContainsEventOfType<T>();

        var actualVersion = DomainEventVersionHelper.GetVersion(typeof(T));
        if (actualVersion != version)
        {
            throw new InvalidOperationException(
                $"Expected event '{typeof(T).Name}' to declare version {version} but found version {actualVersion}.");
        }

        return match;
    }

    /// <summary>
    /// Asserts that <paramref name="events"/> contains exactly <paramref name="n"/> events,
    /// regardless of type.
    /// </summary>
    /// <param name="events">The collection of raised domain events.</param>
    /// <param name="n">The expected total event count.</param>
    /// <exception cref="InvalidOperationException">The actual count does not equal <paramref name="n"/>.</exception>
    public static void HasRaisedExactlyNEvents(this IReadOnlyCollection<IDomainEvent> events, int n)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count != n)
        {
            throw new InvalidOperationException(
                $"Expected exactly {n} domain event(s) but found {events.Count}: " +
                $"[{string.Join(", ", events.Select(e => e.GetType().Name))}].");
        }
    }
}
