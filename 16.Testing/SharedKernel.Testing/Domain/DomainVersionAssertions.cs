using SharedKernel.Domain.Events;

namespace SharedKernel.Testing.Domain;

/// <summary>
/// Plain-exception assertion helpers over <see cref="DomainEventVersionAttribute"/> /
/// <see cref="DomainEventVersionHelper"/>.
/// </summary>
/// <remarks>
/// <see cref="SharedKernel.Testing"/> never depends on FluentAssertions — both members throw
/// plain <see cref="InvalidOperationException"/> with descriptive messages.
/// </remarks>
public static class DomainVersionAssertions
{
    /// <summary>
    /// Asserts that <typeparamref name="TEvent"/> declares <c>[DomainEventVersion(N)]</c> with
    /// <c>N == expectedVersion</c>.
    /// </summary>
    /// <typeparam name="TEvent">The domain event type to inspect.</typeparam>
    /// <param name="expectedVersion">The expected declared schema version.</param>
    /// <exception cref="InvalidOperationException">
    /// The declared version (or the implicit default of 1) does not equal
    /// <paramref name="expectedVersion"/>.
    /// </exception>
    public static void ShouldHaveVersion<TEvent>(int expectedVersion)
        where TEvent : IDomainEvent
    {
        var actual = DomainEventVersionHelper.GetVersion(typeof(TEvent));
        if (actual != expectedVersion)
        {
            throw new InvalidOperationException(
                $"Expected '{typeof(TEvent).Name}' to declare version {expectedVersion} but found version {actual}.");
        }
    }

    /// <summary>
    /// Asserts that <typeparamref name="TEvent"/> carries an explicit
    /// <see cref="DomainEventVersionAttribute"/> declaration (i.e., is not relying on the
    /// implicit default version of 1).
    /// </summary>
    /// <typeparam name="TEvent">The domain event type to inspect.</typeparam>
    /// <exception cref="InvalidOperationException">No <see cref="DomainEventVersionAttribute"/> is present.</exception>
    public static void ShouldBeVersioned<TEvent>()
        where TEvent : IDomainEvent
    {
        var attribute = typeof(TEvent).GetCustomAttributes(typeof(DomainEventVersionAttribute), inherit: false);
        if (attribute.Length == 0)
        {
            throw new InvalidOperationException(
                $"Expected '{typeof(TEvent).Name}' to declare a [DomainEventVersion] attribute, but none was found.");
        }
    }
}
