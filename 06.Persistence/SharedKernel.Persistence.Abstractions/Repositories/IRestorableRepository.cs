using SharedKernel.Domain.Abstractions;
using SharedKernel.Application.Transactions;

namespace SharedKernel.Persistence.Abstractions.Repositories;

/// <summary>
/// Write-side repository contract for reversing a prior soft delete.
/// </summary>
/// <typeparam name="TAggregate">
/// The aggregate root type. Must implement <see cref="IAggregateRoot{TId}"/>.
/// </typeparam>
/// <typeparam name="TId">
/// The aggregate's identity type. Must be non-null.
/// </typeparam>
/// <remarks>
/// <para>
/// Deliberately NOT narrowed to
/// <see cref="SharedKernel.Domain.Abstractions.ISoftDeletable"/> at the interface level — a generic
/// implementing class cannot conditionally satisfy a narrower constraint for only some closed-generic
/// instantiations. The EF Core implementation enforces the real <c>ISoftDeletable</c>-or-not
/// distinction via a runtime guard — mirroring the <c>BulkSpecificationGuard</c>/
/// <c>UnsupportedSpecificationException</c> precedent (compile-time-loose interface +
/// runtime-enforced narrower constraint) rather than a parallel <c>TenantedRepository</c>-style
/// base-class hierarchy.
/// </para>
/// <para>
/// Only STAGES the change — does not call <see cref="IUnitOfWork.SaveChangesAsync"/> itself. A
/// subsequent <c>IUnitOfWork.SaveChangesAsync</c> call persists it, going through the same
/// interceptor/audit/domain-event pipeline a normal update would.
/// </para>
/// </remarks>
public interface IRestorableRepository<TAggregate, TId>
    where TAggregate : IAggregateRoot<TId>
    where TId : notnull
{
    /// <summary>
    /// Stages the reversal of a prior soft delete for <paramref name="aggregate"/> during the next
    /// <see cref="IUnitOfWork.SaveChangesAsync"/> call.
    /// </summary>
    /// <param name="aggregate">The aggregate to restore.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Restoring an aggregate that is already not deleted is an idempotent no-op success.
    /// </remarks>
    Task RestoreAsync(TAggregate aggregate, CancellationToken cancellationToken = default);
}
