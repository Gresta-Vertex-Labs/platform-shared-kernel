namespace SharedKernel.Persistence.Abstractions.UnitOfWork;

/// <summary>
/// Extends <see cref="IUnitOfWork"/> with explicit transaction control.
/// </summary>
/// <remarks>
/// <para>
/// Register this interface only for services that require explicit transaction boundaries —
/// for example, saga compensation patterns or two-phase read-then-write operations where multiple
/// repository calls must be atomic. Services that do not need explicit transactions use
/// <see cref="IUnitOfWork"/> directly.
/// </para>
/// <para>
/// The EF Core implementation (<c>EfTransactionalUnitOfWork</c>) is registered via
/// <c>EfCorePersistenceBuilder.WithTransactionalUnitOfWork()</c>. When that method is called,
/// both <c>IUnitOfWork</c> and <c>ITransactionalUnitOfWork</c> resolve the same scoped instance.
/// </para>
/// <para>
/// <strong>Hard violation:</strong> Application-layer code must inject
/// <c>ITransactionalUnitOfWork</c> — never <c>IDbContextTransaction</c> directly. Injecting
/// the EF Core type couples application code to the ORM and bypasses the abstraction.
/// </para>
/// </remarks>
public interface ITransactionalUnitOfWork : IUnitOfWork
{
    /// <summary>
    /// Opens an explicit database transaction and returns a provider-agnostic transaction handle.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="IPersistenceTransaction"/> that represents the active transaction.
    /// Call <see cref="IPersistenceTransaction.CommitAsync"/> to commit or
    /// <see cref="IPersistenceTransaction.RollbackAsync"/> to roll back.
    /// Always <c>await using</c> the returned handle to release database resources.
    /// </returns>
    /// <remarks>
    /// Multiple repository operations within the returned transaction scope are committed
    /// atomically via <see cref="IPersistenceTransaction.CommitAsync"/>. Domain event dispatch
    /// fires after <c>CommitAsync</c>, consistent with <c>EfUnitOfWork.SaveChangesAsync</c>
    /// semantics.
    /// </remarks>
    Task<IPersistenceTransaction> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>
    /// Executes <paramref name="operation"/> inside an explicit database transaction, using the
    /// provider's retrying execution strategy when one is configured.
    /// </summary>
    /// <param name="operation">
    /// The unit of work to execute inside the transaction. May run MORE THAN ONCE when a retrying
    /// execution strategy is configured — it must be safe to re-run.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// <para>
    /// WO-051/P-320 — the retry-SAFE alternative to <see cref="BeginTransactionAsync"/>'s
    /// handle-based flow. EF Core's retrying execution strategies require the ENTIRE transactional
    /// unit (begin through commit) to run inside one <c>IExecutionStrategy.ExecuteAsync(...)</c>
    /// delegate — the <see cref="BeginTransactionAsync"/> → caller-held
    /// <see cref="IPersistenceTransaction"/> → <c>CommitAsync</c> shape hands control back to
    /// arbitrary caller code in between, which is structurally incompatible with that contract when
    /// retry is enabled. Use this method instead of <see cref="BeginTransactionAsync"/> for any
    /// service that has enabled Npgsql transient-fault retry
    /// (<c>UsePostgreSQL(..., maxRetryCount)</c>).
    /// </para>
    /// <para>
    /// BCL-only signature (<see cref="Func{T,TResult}"/>/<see cref="Task"/>/
    /// <see cref="CancellationToken"/>) — zero ORM types, lives in Abstractions.
    /// </para>
    /// </remarks>
    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken ct = default);

    /// <summary>
    /// Executes <paramref name="operation"/> inside an explicit database transaction and returns its
    /// result, using the provider's retrying execution strategy when one is configured.
    /// </summary>
    /// <typeparam name="TResult">The operation's result type.</typeparam>
    /// <param name="operation">
    /// The unit of work to execute inside the transaction. May run MORE THAN ONCE when a retrying
    /// execution strategy is configured — it must be safe to re-run.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The value returned by <paramref name="operation"/>.</returns>
    /// <remarks>See the non-generic overload's remarks for the full explanation (WO-051/P-320).</remarks>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken ct = default);
}
