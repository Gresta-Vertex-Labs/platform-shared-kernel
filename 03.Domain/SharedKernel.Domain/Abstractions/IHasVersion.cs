namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// Represents a domain aggregate that tracks the number of domain events raised
/// since construction via an integer version counter.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Version"/> is a domain-native optimistic concurrency helper. It starts at
/// <c>0</c> and increments by <c>1</c> on every call to <c>RaiseDomainEvent</c>.
/// <c>ClearDomainEvents()</c> does not decrement <see cref="Version"/> — it is a monotonically
/// increasing counter reflecting the total number of events raised in this aggregate instance's
/// lifetime, not the current size of the pending event queue.
/// </para>
/// <para>
/// This is semantically distinct from <see cref="IHasConcurrency.RowVersion"/>, which is an
/// infrastructure-specific SQL Server opaque byte array managed by the persistence layer.
/// <see cref="IHasVersion.Version"/> is a pure domain concept with no persistence obligation.
/// </para>
/// </remarks>
public interface IHasVersion
{
    /// <summary>
    /// Gets the domain event version counter. Starts at <c>0</c>; increments on every
    /// <c>RaiseDomainEvent</c> call. <c>ClearDomainEvents()</c> does not decrement this value.
    /// </summary>
    int Version { get; }
}
