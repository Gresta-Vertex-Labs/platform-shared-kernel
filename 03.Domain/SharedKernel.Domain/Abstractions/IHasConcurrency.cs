namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// A record protected by optimistic concurrency: a save fails when the stored record changed since it
/// was read.
/// </summary>
public interface IHasConcurrency
{
    /// <summary>Gets the opaque concurrency token of the version that was read.</summary>
    /// <remarks>
    /// Owned by the persistence layer, which maps it to the database's native row version: PostgreSQL's
    /// <c>xmin</c> system column, for example. Domain code never reads or writes it.
    /// </remarks>
    byte[] RowVersion { get; }
}
