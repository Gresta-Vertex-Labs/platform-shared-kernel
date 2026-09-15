using System.Collections.Concurrent;
using System.Reflection;

namespace SharedKernel.Domain.Events;

/// <summary>Reads the declared schema version of a domain event type.</summary>
/// <remarks>
/// Infrastructure (messaging, outbox) uses the version to choose a deserializer or handler. A type without a
/// <see cref="DomainEventVersionAttribute"/> reads as version <c>1</c>; that is a runtime fallback only, because
/// every concrete event should declare its version, which analyzer <c>SK0009</c> enforces. Lookups are cached per
/// type, so calling this for every message is cheap.
/// </remarks>
public static class DomainEventVersionHelper
{
    private static readonly ConcurrentDictionary<Type, int> VersionCache = new();

    /// <summary>
    /// Returns the version declared on <paramref name="domainEventType"/> with
    /// <see cref="DomainEventVersionAttribute"/>, or <c>1</c> when it declares none.
    /// </summary>
    /// <param name="domainEventType">The event type.</param>
    /// <returns>The declared version, or <c>1</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="domainEventType"/> is <see langword="null"/>.</exception>
    public static int GetVersion(Type domainEventType)
    {
        ArgumentNullException.ThrowIfNull(domainEventType);
        return VersionCache.GetOrAdd(
            domainEventType,
            static type => type.GetCustomAttribute<DomainEventVersionAttribute>(inherit: false)?.Version ?? 1);
    }

    /// <summary>Returns the version declared on <typeparamref name="TEvent"/>, or <c>1</c> when it declares none.</summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <returns>The declared version, or <c>1</c>.</returns>
    public static int GetVersion<TEvent>() where TEvent : IDomainEvent => GetVersion(typeof(TEvent));
}
