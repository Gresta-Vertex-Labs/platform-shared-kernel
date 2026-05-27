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
    /// <summary>
    /// Returns the schema version declared on <paramref name="domainEventType"/> via
    /// <see cref="DomainEventVersionAttribute"/>, or <c>1</c> when the attribute is absent.
    /// </summary>
    /// <param name="domainEventType">The CLR type of the domain event to inspect.</param>
    /// <returns>The declared version, or <c>1</c> as the implicit default.</returns>
    public static int GetVersion(Type domainEventType)
    {
        var attribute = domainEventType.GetCustomAttribute<DomainEventVersionAttribute>(inherit: false);
        return attribute?.Version ?? 1;
    }
}
