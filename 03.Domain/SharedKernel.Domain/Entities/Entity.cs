using System.Runtime.CompilerServices;
using SharedKernel.Domain.Abstractions;

namespace SharedKernel.Domain.Entities;

/// <summary>
/// Abstract base class for all domain entities. Equality is identity-based: two entities
/// of the same concrete type are equal if and only if their <see cref="Id"/> values are equal.
/// </summary>
/// <typeparam name="TId">The type of the entity's identity key. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// Transient entities — those whose <see cref="Id"/> equals <c>default(TId)</c> — are never
/// equal to any other instance, including themselves. Their hash code is derived from object
/// identity via <see cref="RuntimeHelpers.GetHashCode"/>.
/// </para>
/// <para>
/// Subclasses must not override <see cref="Equals(object?)"/> or <see cref="GetHashCode()"/>.
/// Equality semantics are sealed at this level.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed record OrderId(Guid Value) : StronglyTypedId&lt;Guid&gt;(Value);
///
/// // Entities extend AggregateRoot (which extends Entity) or Entity directly for child entities.
/// public abstract class OrderLine : Entity&lt;OrderLineId&gt;
/// {
///     protected OrderLine(OrderLineId id) : base(id) { }
///     protected OrderLine() { } // ORM path
/// }
/// </code>
/// </example>
public abstract class Entity<TId> : IEntity<TId>, IEquatable<Entity<TId>> where TId : notnull
{
    /// <summary>Gets the identity key of this entity.</summary>
    public TId Id { get; private init; } = default!;

    /// <summary>
    /// Initialises a new entity with the specified <paramref name="id"/>.
    /// </summary>
    /// <param name="id">The identity key. Must not be the default value for <typeparamref name="TId"/>.</param>
    /// <remarks>
    /// WO-051/P-311 — <paramref name="id"/> is deliberately <strong>unguarded</strong>.
    /// <c>default(TId)</c> is the intentional "transient entity" sentinel consumed by
    /// <see cref="IsTransient"/> — it is not an error condition. Do not add a null/default guard
    /// here; doing so would break every legitimate transient-entity construction path (e.g., an
    /// aggregate factory that assigns its identity only after a successful <c>Result&lt;T&gt;</c>-returning
    /// call, or ORM materialisation before the primary key is known).
    /// </remarks>
    protected Entity(TId id) => Id = id;

    /// <summary>
    /// Protected parameterless constructor for ORM materialisation (e.g., EF Core proxy creation).
    /// Do not call directly in domain code.
    /// </summary>
    protected Entity() { }

    /// <summary>
    /// Returns <see langword="true"/> when this entity has not yet been assigned a permanent identity
    /// (i.e., <see cref="Id"/> equals <c>default(<typeparamref name="TId"/>)</c>).
    /// Transient entities are never equal to any instance including themselves.
    /// </summary>
    public bool IsTransient() => EqualityComparer<TId>.Default.Equals(Id, default!);

    /// <inheritdoc/>
    public sealed override bool Equals(object? obj)
    {
        if (obj is not Entity<TId> other) return false;
        if (IsTransient() || other.IsTransient()) return false;
        if (GetType() != other.GetType()) return false;
        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    /// <inheritdoc/>
    public sealed override int GetHashCode() =>
        IsTransient()
            ? RuntimeHelpers.GetHashCode(this)
            : HashCode.Combine(GetType(), Id);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="other"/> is equal to this entity by identity.
    /// </summary>
    /// <remarks>
    /// WO-051/P-311 — <see cref="IEquatable{T}"/> implementation delegating to
    /// <see cref="Equals(object?)"/>. Purely a boxing/virtual-dispatch-avoidance addition for
    /// generic-collection consumers (<see cref="List{T}.Contains"/>, dictionary keys, LINQ
    /// <c>Distinct</c>/<c>Except</c>) — not a behavior change.
    /// </remarks>
    public bool Equals(Entity<TId>? other) => Equals((object?)other);

    /// <summary>Returns <see langword="true"/> when both entities are equal by identity.</summary>
    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Returns <see langword="true"/> when the entities are not equal by identity.</summary>
    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);
}
