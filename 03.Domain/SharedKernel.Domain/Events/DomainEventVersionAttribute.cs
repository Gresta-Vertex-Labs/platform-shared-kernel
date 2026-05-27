namespace SharedKernel.Domain.Events;

/// <summary>
/// Declares the schema version of a domain event class.
/// Apply this attribute to concrete domain event classes when their schema changes in a
/// backward-incompatible way to enable infrastructure routing to the correct deserializer.
/// </summary>
/// <remarks>
/// <para>
/// Version 1 is implicit — no attribute is required for the initial event schema.
/// Apply <c>[DomainEventVersion(2)]</c> (or higher) only when a backward-incompatible
/// change is introduced.
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
