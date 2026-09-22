using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Specifications;

namespace SharedKernel.Persistence.Abstractions.Repositories;

/// <summary>
/// Set-based changes over every row matching a specification, each one server-side SQL statement that loads
/// no aggregate.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type.</typeparam>
/// <typeparam name="TId">The aggregate's identity type.</typeparam>
/// <remarks>
/// <para>
/// <b>Registered automatically</b> next to <c>IRepository&lt;TAggregate, TId&gt;</c>.
/// </para>
/// <para>
/// <b>What still applies.</b> Tenant isolation (the statement is filtered to the current tenant), the
/// soft-delete filter (unless the specification includes deleted rows), and the audit columns: every update
/// stamps <c>ModifiedOn</c>/<c>ModifiedBy</c> on <see cref="IHasAudit"/> aggregates, and a delete of an
/// <see cref="ISoftDeletable"/> aggregate is a soft delete that stamps <c>IsDeleted</c>/<c>DeletedOn</c>/<c>DeletedBy</c>.
/// </para>
/// <para>
/// <b>What is bypassed.</b> The change tracker, the unit of work's <c>SaveChangesAsync</c>, domain events and
/// field encryption. The statement runs immediately; inside <c>IUnitOfWork.ExecuteInTransactionAsync</c> it
/// joins that transaction. Use it for data maintenance, not for changes other parts of the system must react to.
/// </para>
/// <para>
/// <b>Specification shape.</b> Only criteria and include-deleted are meaningful; includes, ordering and paging
/// are rejected, and a specification without criteria is rejected unless it is
/// <see cref="AllRowsSpecification{T}"/> (a missing WHERE clause is almost always a bug).
/// </para>
/// </remarks>
public interface IBulkMutationRepository<TAggregate, TId>
    where TAggregate : class, IAggregateRoot<TId>
    where TId : notnull
{
    /// <summary>Updates every row matching <paramref name="spec"/> in one <c>UPDATE</c> statement.</summary>
    /// <param name="spec">The rows to update.</param>
    /// <param name="setters">
    /// The columns to set, such as <c>s =&gt; s.SetProperty(o =&gt; o.Status, OrderStatus.Archived)</c>. Complex-type
    /// members (<c>o =&gt; o.Contact.Email</c>) are allowed.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of rows updated.</returns>
    /// <exception cref="SharedKernel.Core.Exceptions.SharedKernelException">(a validation error, code <c>Persistence.BulkMutation.UnsupportedSpecification</c>):
    /// The specification has an unsupported shape, or a setter targets something other than a mapped property
    /// or a protected column: the key, a concurrency token, <c>TenantId</c>, the creation audit columns, or an
    /// encrypted column (which would be written as plaintext).
    /// </exception>
    Task<int> ExecuteUpdateAsync(
        ISpecification<TAggregate> spec,
        Action<BulkUpdateSetters<TAggregate>> setters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every row matching <paramref name="spec"/>: a soft delete (one <c>UPDATE</c>) for an
    /// <see cref="ISoftDeletable"/> aggregate, otherwise a physical <c>DELETE</c>.
    /// </summary>
    /// <param name="spec">The rows to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of rows deleted; rows already soft-deleted are left as they are and not counted.</returns>
    /// <exception cref="SharedKernel.Core.Exceptions.SharedKernelException">The specification has an unsupported shape (a validation error, code <c>Persistence.BulkMutation.UnsupportedSpecification</c>).</exception>
    Task<int> ExecuteDeleteAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default);

    /// <summary>
    /// Physically deletes every row matching <paramref name="spec"/> in one <c>DELETE</c> statement, soft-deletable
    /// or not.
    /// </summary>
    /// <param name="spec">
    /// The rows to purge. For soft-deleted rows, set <see cref="ISpecification{T}.IncludeDeleted"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of rows removed.</returns>
    /// <exception cref="SharedKernel.Core.Exceptions.SharedKernelException">The specification has an unsupported shape (a validation error, code <c>Persistence.BulkMutation.UnsupportedSpecification</c>).</exception>
    /// <remarks>Irreversible. Use it for retention jobs and data-subject erasure.</remarks>
    Task<int> ExecutePurgeAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default);
}
