namespace SharedKernel.Domain.Events;

/// <summary>
/// Declares the schema version of a domain event class, so infrastructure can route each event to
/// the correct deserializer or handler.
/// </summary>
/// <remarks>
/// <para>
/// Declare it on every concrete domain event, starting with <c>[DomainEventVersion(1)]</c> for the
/// initial schema, and increment it when a backward-incompatible change is introduced. The platform
/// enforces this: analyzer <c>SK0009</c> (<c>SharedKernel.Analyzers</c>) flags an event type without
/// it. An explicit version makes the first breaking change a visible, reviewable edit of an existing
/// number rather than the easy-to-forget addition of an attribute nobody had to write before.
/// </para>
/// <para>
/// <see cref="DomainEventVersionHelper.GetVersion(Type)"/> still returns <c>1</c> for a type without
/// the attribute. That is a runtime fallback for events declared outside the analyzer's reach, not a
/// licence to omit the declaration.
/// </para>
/// <para>
/// Infrastructure (messaging, outbox) reads the version via
/// <see cref="DomainEventVersionHelper.GetVersion(Type)"/> to route events to the correct
/// deserializer or handler registration. The <see cref="IDomainEvent"/> interface is unchanged
/// — no runtime <c>Version</c> property is added to domain events themselves.
/// </para>
/// <para>
/// This attribute is non-inherited and cannot be applied multiple times to the same class.
/// Apply it only to the concrete sealed event record, not to abstract base records.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [DomainEventVersion(1)]
/// public sealed record OrderPlacedEvent : DomainEvent
/// {
///     public required Guid OrderId { get; init; }
///     public required decimal Total { get; init; }
/// }
///
/// [DomainEventVersion(2)]
/// public sealed record OrderPlacedEventV2 : DomainEvent
/// {
///     public required Guid OrderId { get; init; }
///     public required decimal Total { get; init; }
///     public required string Currency { get; init; } // new field in v2
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class DomainEventVersionAttribute : Attribute
{
    /// <summary>
    /// Initialises a new <see cref="DomainEventVersionAttribute"/> with the specified
    /// <paramref name="version"/>.
    /// </summary>
    /// <param name="version">The schema version. Must be greater than or equal to 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="version"/> is less than 1.
    /// </exception>
    public DomainEventVersionAttribute(int version)
    {
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version), version,
                "Domain event version must be greater than or equal to 1.");
        Version = version;
    }

    /// <summary>Gets the declared schema version of the domain event.</summary>
    public int Version { get; }
}
