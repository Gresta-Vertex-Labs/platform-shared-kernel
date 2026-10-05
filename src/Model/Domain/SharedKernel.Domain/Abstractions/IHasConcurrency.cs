namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// A record protected by optimistic concurrency: saving it fails when the stored row changed after it
/// was read.
/// </summary>
/// <remarks>
/// <b>Pitfall.</b> Do not confuse <see cref="RowVersion"/> with <see cref="IHasVersion.Version"/>, the
/// event sequence number, which changes only when an event is raised.
/// </remarks>
public interface IHasConcurrency
{
    /// <summary>Gets the opaque concurrency token of the row as it was read.</summary>
    /// <remarks>
    /// The persistence layer owns this value and maps it to the database's native row version, such as
    /// PostgreSQL's <c>xmin</c> system column. It changes on every write. Domain code never reads or
    /// writes it.
    /// </remarks>
    byte[] RowVersion { get; }
}
