namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// Marks a type as a domain entity with a strongly-typed, non-null identity key of type <typeparamref name="TId"/>.
/// </summary>
/// <typeparam name="TId">The type of the identity key. Must be non-null.</typeparam>
public interface IEntity<TId> where TId : notnull
{
}
