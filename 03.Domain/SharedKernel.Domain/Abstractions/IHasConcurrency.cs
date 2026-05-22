namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// Marks a type as supporting optimistic concurrency control via a row-version token.
/// The <see cref="RowVersion"/> property is managed exclusively by the persistence layer.
/// </summary>
public interface IHasConcurrency
{
    /// <summary>Gets the opaque concurrency token assigned by the database on each write.</summary>
    byte[] RowVersion { get; }
}
