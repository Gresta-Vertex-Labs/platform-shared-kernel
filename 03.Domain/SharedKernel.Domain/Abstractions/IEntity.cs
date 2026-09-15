namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// A domain entity: an object defined by a stable identity rather than by its attribute values.
/// </summary>
/// <typeparam name="TId">The identity key type. Must be non-null.</typeparam>
/// <remarks>
/// Infrastructure that works across entity types (repositories, change tracking, outbox
/// correlation) reads the identity through <see cref="Id"/> instead of reflecting over a
/// property name.
/// </remarks>
public interface IEntity<out TId> where TId : notnull
{
    /// <summary>Gets the identity key of this entity.</summary>
    /// <remarks>
    /// Equals <c>default(TId)</c> while the entity is transient, meaning its identity has not
    /// been assigned yet.
    /// </remarks>
    TId Id { get; }
}
