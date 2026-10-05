using System.Data;

namespace SharedKernel.Execution.Transactions;

/// <summary>
/// The single commit boundary of a service: saves staged changes, and runs a whole unit of work
/// inside one database transaction that a retrying execution strategy can replay.
/// </summary>
/// <remarks>
/// <para>
/// One contract for both layers. <c>SharedKernel.Application.Pipeline</c>' <c>TransactionBehavior</c>
/// runs every outermost command through <see cref="ExecuteInTransactionAsync{TResult}(Func{CancellationToken,Task{TResult}},CancellationToken)"/>;
/// <c>SharedKernel.Persistence.EfCore</c> implements it (<c>EfUnitOfWork</c>). There is no second,
/// "transactional" variant and no handle-based <c>BeginTransactionAsync</c>: a caller-held transaction
/// handle cannot be replayed by a retrying execution strategy, a delegate can.
/// </para>
/// <para>
/// <strong>The operation may run more than once.</strong> When the implementation retries a transient
/// failure, it discards everything the failed attempt staged and invokes the delegate again. Load
/// what the operation needs inside the delegate; never close over an entity loaded outside it.
/// Side effects outside the database (HTTP calls, messages) do not belong inside the delegate —
/// queue them for after the commit instead (<c>ICommandScope.OnCompleted</c> in
/// <c>SharedKernel.Application.Pipeline</c>).
/// </para>
/// <para>
/// <strong>Nesting.</strong> Calling an <c>ExecuteInTransactionAsync</c> overload while a transaction
/// is already active joins it — also through the unit of work of another context of the same
/// service: the operation runs, its changes are saved, and the outermost call commits or rolls back
/// everything together. A joined operation that fails (returns a failed <c>Result</c> or throws) marks
/// the transaction <em>rollback-only</em>: nothing commits, and an outermost operation that still
/// completes successfully gets a <see cref="TransactionRolledBackException"/>.
/// </para>
/// <para>
/// <strong>Several contexts.</strong> The transaction covers every context of the service resolved in
/// the same scope that reaches the same database: they share its connection and transaction, and every
/// one with changes is saved before the commit. A context on another database cannot join; if it holds
/// changes when the transaction commits, the commit is refused (rolled back) with an
/// <see cref="InvalidOperationException"/> instead of leaving those changes unsaved.
/// </para>
/// <para>
/// <strong>Ambiguous commit.</strong> When the <c>COMMIT</c> itself fails without a response (connection
/// loss, timeout), the outcome is unknown: the call throws <see cref="CommitOutcomeUnknownException"/>
/// and is never retried.
/// </para>
/// </remarks>
#pragma warning disable RS0026 // Symbol has multiple public overloads with optional parameters.
// The isolation-level overloads take isolationLevel as a required parameter, so the argument count
// alone selects the overload; adding a trailing optional parameter to either can never silently
// rebind an existing call site. Flattening them into one method would force every caller to pass
// "null for the parameters you don't need".
public interface IUnitOfWork
{
    /// <summary>
    /// Gets a value indicating whether an <c>ExecuteInTransactionAsync</c> call is currently running on
    /// this unit of work — that is, whether <see cref="OnBeforeCommit"/> may be called.
    /// </summary>
    bool IsTransactionActive { get; }

    /// <summary>Saves every staged change.</summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The number of state entries written.</returns>
    /// <remarks>
    /// Inside an active transaction the changes are written within it and committed with it;
    /// outside one, the save is its own transaction — covering every context of the scope that has
    /// changes, so a change staged through another context's repository is never left behind.
    /// </remarks>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside one transaction, saves what it staged, runs every
    /// <see cref="OnBeforeCommit"/> callback and commits. An exception rolls everything back.
    /// </summary>
    /// <param name="operation">The unit of work. May run more than once — see the type remarks.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside one transaction at <paramref name="isolationLevel"/>.
    /// </summary>
    /// <param name="operation">The unit of work. May run more than once — see the type remarks.</param>
    /// <param name="isolationLevel">The isolation level, or <see langword="null"/> for the provider default.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <remarks>
    /// The isolation level applies only when this call starts the transaction; a call that joins an
    /// active transaction runs at that transaction's level.
    /// </remarks>
    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        IsolationLevel? isolationLevel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside one transaction and returns its result.
    /// </summary>
    /// <typeparam name="TResult">The operation's result type.</typeparam>
    /// <param name="operation">The unit of work. May run more than once — see the type remarks.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The value <paramref name="operation"/> returned.</returns>
    /// <remarks>
    /// <para>
    /// When the result is a failure on the platform's result railway — it implements
    /// <see cref="SharedKernel.Primitives.Results.IHasSuccessFlag"/> and
    /// <see cref="SharedKernel.Primitives.Results.IHasSuccessFlag.IsSuccess"/> is
    /// <see langword="false"/>, as a failed <c>Result</c>/<c>Result&lt;T&gt;</c> is — nothing is
    /// saved, no <see cref="OnBeforeCommit"/> callback runs, the transaction rolls back and the
    /// failure is returned. Any other result commits.
    /// </para>
    /// <para>
    /// Joining an active transaction: a failure result is returned without saving and marks the
    /// transaction rollback-only, so the outermost call rolls everything back.
    /// </para>
    /// </remarks>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside one transaction at <paramref name="isolationLevel"/>
    /// and returns its result, with the same failure-result rule as
    /// <see cref="ExecuteInTransactionAsync{TResult}(Func{CancellationToken,Task{TResult}},CancellationToken)"/>.
    /// </summary>
    /// <typeparam name="TResult">The operation's result type.</typeparam>
    /// <param name="operation">The unit of work. May run more than once — see the type remarks.</param>
    /// <param name="isolationLevel">The isolation level, or <see langword="null"/> for the provider default.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The value <paramref name="operation"/> returned.</returns>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        IsolationLevel? isolationLevel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues <paramref name="callback"/> to run inside the active transaction, after the operation's
    /// changes are saved and immediately before the commit.
    /// </summary>
    /// <param name="callback">
    /// The work to run. It shares the transaction, so whatever it writes commits or rolls back with
    /// the operation. An exception from it rolls the transaction back and propagates.
    /// </param>
    /// <exception cref="InvalidOperationException">No transaction is active (<see cref="IsTransactionActive"/> is <see langword="false"/>).</exception>
    /// <remarks>
    /// <para>
    /// Callbacks run once per successful attempt, in registration order, only when the outermost
    /// transaction is about to commit. Callbacks registered during an attempt that is retried are
    /// discarded with that attempt — the retried operation registers them again.
    /// </para>
    /// <para>
    /// The audit trail uses this to write a <see cref="Auditing.AuditOutcome.Succeeded"/> record in the
    /// same transaction as the change it describes.
    /// </para>
    /// </remarks>
    void OnBeforeCommit(Func<CancellationToken, Task> callback);
}
#pragma warning restore RS0026
