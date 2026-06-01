namespace SharedKernel.Persistence.Abstractions.UnitOfWork;

/// <summary>
/// Defines the single, provider-agnostic commit boundary for a unit of work.
/// </summary>
/// <remarks>
/// <para>
/// <c>IUnitOfWork.SaveChangesAsync</c> is the <strong>only</strong> permitted save boundary.
/// Calling <c>DbContext.SaveChanges[Async]</c> directly anywhere outside the EF Core implementation
/// of this interface is a hard violation of the persistence architecture rules.
/// </para>
/// <para>
/// All three EF Core interceptors (Audit, SoftDelete, Concurrency) fire automatically within the
/// <c>EfUnitOfWork</c> implementation before the database commit is issued.
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>
    /// Commits all staged changes to the backing store within the current unit of work boundary.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The number of state entries written to the database. Zero is a valid result when no
    /// entities were modified.
    /// </returns>
    /// <exception cref="SharedKernel.Core.Exceptions.ConflictException">
    /// Thrown when an optimistic concurrency conflict is detected for an
    /// <see cref="SharedKernel.Domain.Abstractions.IHasConcurrency"/> entity.
    /// </exception>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
