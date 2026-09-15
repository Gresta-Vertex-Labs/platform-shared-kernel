using System.Collections.Concurrent;
using System.Reflection;

namespace SharedKernel.Domain.Events;

/// <summary>
/// Reads the schema version a domain event type declares with <see cref="DomainEventVersionAttribute"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Usage.</b> Infrastructure such as an outbox or a message serializer calls it to choose the deserializer
/// or handler for an event.
/// </para>
/// <para>
/// <b>Fallback.</b> A type without the attribute, including one that only inherits it, reads as version
/// <c>1</c>. The fallback exists for event types outside the analyzer's reach; it is not a reason to omit the
/// attribute.
/// </para>
/// <para>
/// <b>Thread safety.</b> Results are cached per type in a concurrent dictionary, so calling it for every
/// message is cheap and safe from any thread.
/// </para>
/// </remarks>
public static class DomainEventVersionHelper
{
    private static readonly ConcurrentDictionary<Type, int> VersionCache = new();

    /// <summary>
    /// Returns the version declared on <paramref name="domainEventType"/>, or <c>1</c> when it declares none.
    /// </summary>
    /// <param name="domainEventType">The event type to inspect. Must not be null.</param>
    /// <returns>The declared version; <c>1</c> when the type itself carries no attribute.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="domainEventType"/> is <see langword="null"/>.
    /// </exception>
    public static int GetVersion(Type domainEventType)
    {
        ArgumentNullException.ThrowIfNull(domainEventType);
        return VersionCache.GetOrAdd(
            domainEventType,
            static type => type.GetCustomAttribute<DomainEventVersionAttribute>(inherit: false)?.Version ?? 1);
    }

    /// <summary>
    /// Returns the version declared on <typeparamref name="TEvent"/>, or <c>1</c> when it declares none.
    /// </summary>
    /// <typeparam name="TEvent">The event type to inspect.</typeparam>
    /// <returns>The declared version; <c>1</c> when the type itself carries no attribute.</returns>
    public static int GetVersion<TEvent>() where TEvent : IDomainEvent => GetVersion(typeof(TEvent));
}
