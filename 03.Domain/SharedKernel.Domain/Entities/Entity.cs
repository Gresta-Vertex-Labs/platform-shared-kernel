using System.Runtime.CompilerServices;
using SharedKernel.Domain.Abstractions;

namespace SharedKernel.Domain.Entities;

/// <summary>
/// Base class for domain entities. Equality is by identity: two entities of the same concrete type
/// are equal when their <see cref="Id"/> values are equal.
/// </summary>
/// <typeparam name="TId">The identity key type. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// <b>Transient entities.</b> An entity whose <see cref="Id"/> is still <c>default(TId)</c> has no
/// identity yet. It is equal only to itself, never to another transient instance, so two new,
/// unsaved entities are always distinct while one instance can still be found in and removed from a
/// collection.
/// </para>
/// <para>
/// <b>Hash codes change when an identity is assigned.</b> A transient entity hashes by reference;
/// once the database assigns its key, it hashes by <see cref="Id"/>. Do not keep a transient entity
/// in a <see cref="HashSet{T}"/> or dictionary key across the save that assigns its identity.
/// </para>
/// <para>
/// <b>Runtime type is part of equality.</b> Entities of different concrete types never compare equal,
/// even with the same key. A lazy-loading proxy has a different runtime type from the entity it wraps,
/// so compare identities with <see cref="Id"/> when proxies are enabled.
/// </para>
/// <para>Equality is sealed at this level; subclasses cannot override it.</para>
/// </remarks>
/// <example>
/// <code>
/// public sealed record OrderLineId(Guid Value) : StronglyTypedId&lt;Guid&gt;(Value);
///
/// public sealed class OrderLine : Entity&lt;OrderLineId&gt;
/// {
///     public OrderLine(OrderLineId id, int quantity) : base(id) =&gt; Quantity = quantity;
///
///     private OrderLine() { } // ORM
///
///     public int Quantity { get; private set; }
/// }
/// </code>
/// </example>
public abstract class Entity<TId> : IEntity<TId>, IEquatable<Entity<TId>>
    where TId : notnull
{
    /// <summary>Initializes an entity with the specified identity key.</summary>
    /// <param name="id">
    /// The identity key. <c>default(TId)</c> is allowed and makes the entity transient: an identity
    /// assigned later, typically by the database.
    /// </param>
    protected Entity(TId id) => Id = id;

    /// <summary>Initializes an entity for ORM materialization. Do not call from domain code.</summary>
    protected Entity() => Id = default!;

    /// <inheritdoc/>
    public TId Id { get; private init; }

    /// <summary>
    /// Returns <see langword="true"/> when this entity has no identity yet, meaning <see cref="Id"/>
    /// equals <c>default(TId)</c>.
    /// </summary>
    /// <returns><see langword="true"/> for a transient entity; otherwise <see langword="false"/>.</returns>
    public bool IsTransient() => EqualityComparer<TId>.Default.Equals(Id, default!);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="obj"/> is this same instance, or an entity of
    /// the same concrete type with an equal, non-default <see cref="Id"/>.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when the two are the same entity; otherwise <see langword="false"/>.</returns>
    public sealed override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj))
            return true;

        if (obj is not Entity<TId> other || GetType() != other.GetType())
            return false;

        if (IsTransient() || other.IsTransient())
            return false;

        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="other"/> is the same entity.</summary>
    /// <param name="other">The entity to compare with.</param>
    /// <returns><see langword="true"/> when the two are the same entity; otherwise <see langword="false"/>.</returns>
    public bool Equals(Entity<TId>? other) => Equals((object?)other);

    /// <summary>
    /// Returns a hash code from the runtime type and <see cref="Id"/>, or from the instance reference
    /// while the entity is transient.
    /// </summary>
    /// <returns>The hash code.</returns>
    public sealed override int GetHashCode() =>
        IsTransient() ? RuntimeHelpers.GetHashCode(this) : HashCode.Combine(GetType(), Id);

    /// <summary>Returns <see langword="true"/> when both operands are the same entity, or both are null.</summary>
    /// <param name="left">The first entity.</param>
    /// <param name="right">The second entity.</param>
    /// <returns><see langword="true"/> when equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Returns <see langword="true"/> when the operands are not the same entity.</summary>
    /// <param name="left">The first entity.</param>
    /// <param name="right">The second entity.</param>
    /// <returns><see langword="true"/> when not equal; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);
}
