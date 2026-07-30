using System.Collections.Concurrent;
using System.Reflection;

namespace SharedKernel.Domain.Events;

/// <summary>
/// Reads the declared schema version of a domain event type.
/// </summary>
/// <remarks>
/// Use this helper in infrastructure code (messaging, outbox) to determine which
/// deserializer or handler registration to invoke for a given domain event type.
/// Version 1 is implicit and does not require a <see cref="DomainEventVersionAttribute"/>
/// declaration.
/// </remarks>
public static class DomainEventVersionHelper
{
    // WO-051/P-311 — caches the reflection lookup per distinct Type so GetCustomAttribute runs at
    // most once per Type for the process lifetime. Unlike StronglyTypedIdJsonConverterFactory
    // (already cached by JsonSerializerOptions), this helper is called directly by infrastructure
    // (messaging/outbox) on a potential per-message hot path with no caller-side cache of its own.
    private static readonly ConcurrentDictionary<Type, int> _versionCache = new();

    /// <summary>
    /// Returns the schema version declared on <paramref name="domainEventType"/> via
    /// <see cref="DomainEventVersionAttribute"/>, or <c>1</c> when the attribute is absent.
    /// </summary>
    /// <param name="domainEventType">The CLR type of the domain event to inspect.</param>
    /// <returns>The declared version, or <c>1</c> as the implicit default.</returns>
    public static int GetVersion(Type domainEventType) =>
        _versionCache.GetOrAdd(domainEventType, static t =>
            t.GetCustomAttribute<DomainEventVersionAttribute>(inherit: false)?.Version ?? 1);
}
