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
#pragma warning disable RS0026 // Symbol has multiple public overloads with optional parameters.
// The two ExecuteInTransactionAsync overloads (and their <TResult> counterparts below) are never
// ambiguous to a caller: the simple overload takes exactly one required parameter (operation) plus
// the trailing optional cancellationToken, while the isolation-level overload requires isolationLevel
// and verifySucceeded as its own MANDATORY (non-defaulted, nullable-but-required) parameters — a call
// site's argument COUNT alone already picks the correct overload, so no future addition to either
// overload's optional-parameter list can silently rebind an existing call site to the other one. This
// is the same overload shape RS0026's own linked guidance calls out as safe; suppressed rather than
// redesigned to avoid flattening two genuinely different call shapes into one method with unreadable
// "pass null for the parameters you don't need" ergonomics.
public interface ITransactionalUnitOfWork : IUnitOfWork
{
    /// <summary>
    /// Opens an explicit database transaction and returns a provider-agnostic transaction handle.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An <see cref="IPersistenceTransaction"/> that represents the active transaction.
    /// Call <see cref="IPersistenceTransaction.CommitAsync"/> to commit or
    /// <see cref="IPersistenceTransaction.RollbackAsync"/> to roll back.
    /// Always <c>await using</c> the returned handle to release database resources.
    /// </returns>
    /// <remarks>
    /// Multiple repository operations within the returned transaction scope are committed
    /// atomically via <see cref="IPersistenceTransaction.CommitAsync"/>. Domain event dispatch
    /// happens inside each <c>IUnitOfWork.SaveChangesAsync</c> call made before <c>CommitAsync</c>,
    /// not as a separate post-commit step.
    /// </remarks>
    Task<IPersistenceTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens an explicit database transaction at a specific <paramref name="isolationLevel"/> and
    /// returns a provider-agnostic transaction handle.
    /// </summary>
    /// <param name="isolationLevel">
    /// The transaction isolation level, or <see langword="null"/> to use the provider's default.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>See <see cref="BeginTransactionAsync(CancellationToken)"/>.</returns>
    Task<IPersistenceTransaction> BeginTransactionAsync(
        System.Data.IsolationLevel? isolationLevel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes <paramref name="operation"/> inside an explicit database transaction, using the
    /// provider's retrying execution strategy when one is configured.
    /// </summary>
    /// <param name="operation">
    /// The unit of work to execute inside the transaction. May run MORE THAN ONCE when a retrying
    /// execution strategy is configured — it must be safe to re-run.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// <para>
    /// The retry-SAFE alternative to <see cref="BeginTransactionAsync(CancellationToken)"/>'s
    /// handle-based flow. EF Core's retrying execution strategies require the ENTIRE transactional
    /// unit (begin through commit) to run inside one <c>IExecutionStrategy.ExecuteAsync(...)</c>
    /// delegate — the <see cref="BeginTransactionAsync(CancellationToken)"/> → caller-held
    /// <see cref="IPersistenceTransaction"/> → <c>CommitAsync</c> shape hands control back to
    /// arbitrary caller code in between, which is structurally incompatible with that contract when
    /// retry is enabled. Use this method instead of <see cref="BeginTransactionAsync(CancellationToken)"/>
    /// for any service that has enabled Npgsql transient-fault retry
    /// (<c>UsePostgreSQL(..., maxRetryCount)</c>).
    /// </para>
    /// <para>
    /// <strong>Re-run safety:</strong> the EF Core implementation clears its change
    /// tracker at the start of every attempt (including the first), so <paramref name="operation"/>
    /// must fetch/re-fetch whatever entities it needs through a repository — never close over an
    /// entity instance obtained outside this delegate.
    /// </para>
    /// <para>
    /// BCL-only signature (<see cref="Func{T,TResult}"/>/<see cref="Task"/>/
    /// <see cref="CancellationToken"/>) — zero ORM types, lives in Abstractions.
    /// </para>
    /// </remarks>
    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes <paramref name="operation"/> inside an explicit database transaction at a specific
    /// <paramref name="isolationLevel"/>, using the provider's retrying execution strategy when one
    /// is configured, with an optional post-exhaustion success check.
    /// </summary>
    /// <param name="operation">See <see cref="ExecuteInTransactionAsync(Func{CancellationToken,Task},CancellationToken)"/>.</param>
    /// <param name="isolationLevel">
    /// The transaction isolation level, or <see langword="null"/> for the provider's default.
    /// </param>
    /// <param name="verifySucceeded">
    /// Optional. Invoked once, ONLY when every retry attempt has been exhausted, to check whether
    /// the operation actually succeeded server-side despite the client never observing a successful
    /// acknowledgement — a real risk for a non-idempotent retrying operation. Returning
    /// <see langword="true"/> suppresses the exhaustion failure instead of propagating a false
    /// failure that would cause a caller to retry an operation that already committed.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        System.Data.IsolationLevel? isolationLevel,
        Func<CancellationToken, Task<bool>>? verifySucceeded,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes <paramref name="operation"/> inside an explicit database transaction and returns its
    /// result, using the provider's retrying execution strategy when one is configured.
    /// </summary>
    /// <typeparam name="TResult">The operation's result type.</typeparam>
    /// <param name="operation">
    /// The unit of work to execute inside the transaction. May run MORE THAN ONCE when a retrying
    /// execution strategy is configured — it must be safe to re-run.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The value returned by <paramref name="operation"/>.</returns>
    /// <remarks>See the non-generic overload's remarks for the full explanation.</remarks>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes <paramref name="operation"/> inside an explicit database transaction at a specific
    /// <paramref name="isolationLevel"/> and returns its result, using the provider's retrying
    /// execution strategy when one is configured.
    /// </summary>
    /// <typeparam name="TResult">The operation's result type.</typeparam>
    /// <param name="operation">See <see cref="ExecuteInTransactionAsync{TResult}(Func{CancellationToken,Task{TResult}},CancellationToken)"/>.</param>
    /// <param name="isolationLevel">
    /// The transaction isolation level, or <see langword="null"/> for the provider's default.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The value returned by <paramref name="operation"/>.</returns>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        System.Data.IsolationLevel? isolationLevel,
        CancellationToken cancellationToken = default);
}
#pragma warning restore RS0026
