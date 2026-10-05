namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// An entity: a domain object defined by a stable identity rather than by its attribute values.
/// </summary>
/// <typeparam name="TId">The identity key type. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// <b>Usage.</b> Extend <see cref="SharedKernel.Domain.Entities.Entity{TId}"/>, which supplies
/// identity-based equality, rather than implementing this interface directly.
/// </para>
/// <para>
/// <b>Infrastructure.</b> Code that works across entity types, such as repositories and change
/// tracking, reads the identity through <see cref="Id"/> instead of reflecting over a property name.
/// </para>
/// </remarks>
public interface IEntity<out TId> where TId : notnull
{
    /// <summary>
    /// Gets the identity key; <c>default(TId)</c> marks a transient entity whose identity has not been
    /// assigned yet.
    /// </summary>
    TId Id { get; }
}
