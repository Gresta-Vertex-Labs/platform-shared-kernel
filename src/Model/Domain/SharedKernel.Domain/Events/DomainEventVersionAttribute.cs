namespace SharedKernel.Domain.Events;

/// <summary>
/// Declares the schema version of a concrete domain event type, so infrastructure can pick the matching
/// deserializer or handler.
/// </summary>
/// <remarks>
/// <para>
/// <b>Usage.</b> Apply it to every concrete event, starting at <c>[DomainEventVersion(1)]</c>, and increment
/// it with every backward-incompatible change to the event's shape. Analyzer <c>SK0009</c> flags a
/// non-abstract event type that lacks it. Infrastructure reads the value with
/// <see cref="DomainEventVersionHelper.GetVersion(Type)"/>; events carry no version property of their own.
/// </para>
/// <para>
/// <b>Pitfall.</b> The attribute is not inherited. Apply it to the concrete sealed record itself: a type
/// that only inherits it from a base record reads as version <c>1</c>.
/// </para>
/// <para>
/// <b>Validation.</b> A version below 1 compiles, but the constructor throws when the attribute is read
/// through reflection.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Version 2: Total changed from decimal to Money, which breaks existing readers.
/// [DomainEventVersion(2)]
/// public sealed record OrderPlaced(OrderId OrderId, Money Total) : DomainEvent;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class DomainEventVersionAttribute : Attribute
{
    /// <summary>Initializes a new attribute declaring schema version <paramref name="version"/>.</summary>
    /// <param name="version">The schema version. Must be 1 or greater.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is less than 1.</exception>
    public DomainEventVersionAttribute(int version)
    {
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version), version,
                "Domain event version must be greater than or equal to 1.");
        Version = version;
    }

    /// <summary>Gets the declared schema version; always 1 or greater.</summary>
    public int Version { get; }
}
