using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.UnitOfWork;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Options;

namespace SharedKernel.Persistence.EfCore.UnitOfWork;

/// <summary>
/// EF Core implementation of <see cref="ITransactionalUnitOfWork"/> that wraps an explicit
/// database transaction via <c>DbContext.Database.BeginTransactionAsync</c>.
/// </summary>
/// <remarks>
/// <para>
/// Register this class only when the consuming service calls
/// <c>EfCorePersistenceBuilder.WithTransactionalUnitOfWork()</c>. When registered, both
/// <c>IUnitOfWork</c> and <c>ITransactionalUnitOfWork</c> resolve the same scoped instance.
/// </para>
/// <para>
/// <strong>Domain event dispatch:</strong> <see cref="SaveChangesAsync"/> dispatches
/// pre-commit, exactly like <see cref="EfUnitOfWork"/> — see <see cref="DomainEventDispatchLoop"/>.
/// This is unconditional now, whether or not an explicit transaction is active: because dispatch
/// happens BEFORE the physical save (not after, as before this phase), there is no longer a
/// "defer dispatch until the explicit transaction commits" special case to get right — each
/// <see cref="SaveChangesAsync"/> call is already atomic with its own dispatch, and an explicit
/// transaction spanning multiple <see cref="SaveChangesAsync"/> calls simply commits all of them
/// together at the database level.
/// </para>
/// <para>
/// <strong>Hard violation:</strong> Application-layer code must inject
/// <c>ITransactionalUnitOfWork</c> — never <c>IDbContextTransaction</c> directly.
/// </para>
/// </remarks>
#pragma warning disable RS0026 // Symbol has multiple public overloads with optional parameters.
// See ITransactionalUnitOfWork's identical suppression for the full rationale — this class's
// overloads mirror that interface's exactly, so the same "argument count alone selects the
// overload, never ambiguous" reasoning applies here unchanged.
public sealed class EfTransactionalUnitOfWork : ITransactionalUnitOfWork
{
    private readonly SharedKernelDbContext _dbContext;
    private readonly IDomainEventDispatcher? _dispatcher;
    private readonly ILogger<EfTransactionalUnitOfWork> _logger;
    private readonly TransientFaultRetryOptions? _retryOptions;
    private readonly SharedKernel.Persistence.Abstractions.Coordination.IAmbientDbTransaction? _ambientTransactionAccessor;

    /// <summary>
    /// Initialises a new <see cref="EfTransactionalUnitOfWork"/>.
    /// </summary>
    /// <param name="dbContext">The scoped shared-kernel DB context.</param>
    /// <param name="dispatcher">
    /// Optional dispatcher for domain events raised during the save cycle.
    /// When <see langword="null"/>, events are cleared but not dispatched.
    /// </param>
    /// <param name="logger">
    /// Optional logger for the <c>TransientRetryExhausted</c> Warning (EventId <c>6008</c>) —
    /// see <see cref="EfUnitOfWork"/>'s remarks for the full rationale, which
    /// applies identically here. Resolved by DI when registered; falls back to
    /// <see cref="NullLogger{T}"/> otherwise.
    /// </param>
    /// <param name="retryOptions">
    /// Optional discoverability options registered by
    /// <c>EfCorePersistenceBuilder.WithTransientFaultRetry(...)</c>, consulted only to compute the
    /// logged <c>AttemptCount</c> value — see <see cref="EfUnitOfWork"/>'s equivalent parameter.
    /// </param>
    /// <param name="ambientTransactionAccessor">
    /// Optional scoped accessor published to while an explicit transaction is active, so a
    /// Dapper command service resolving <c>IAmbientDbTransaction</c> can enlist in the same
    /// transaction. Registered by <c>EfCorePersistenceBuilder.WithTransactionalUnitOfWork()</c>;
    /// <see langword="null"/> when that method was not called.
    /// </param>
    public EfTransactionalUnitOfWork(
        SharedKernelDbContext dbContext,
        IDomainEventDispatcher? dispatcher = null,
        ILogger<EfTransactionalUnitOfWork>? logger = null,
        TransientFaultRetryOptions? retryOptions = null,
        SharedKernel.Persistence.Abstractions.Coordination.IAmbientDbTransaction? ambientTransactionAccessor = null)
    {
        _dbContext = dbContext;
        _dispatcher = dispatcher;
        _logger = logger ?? NullLogger<EfTransactionalUnitOfWork>.Instance;
        _retryOptions = retryOptions;
        _ambientTransactionAccessor = ambientTransactionAccessor;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Dispatches pre-commit, unconditionally — see class remarks.
    /// </remarks>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await DomainEventDispatchLoop.RunAsync(_dbContext, _dispatcher, cancellationToken);
        return await ExecuteWithRetryLoggingAsync(() => _dbContext.SaveChangesAsync(cancellationToken));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Opens an EF Core database transaction via <c>DbContext.Database.BeginTransactionAsync</c>
    /// and wraps it in an <see cref="EfPersistenceTransaction"/> adapter that implements
    /// <see cref="IPersistenceTransaction"/>. Domain event dispatch happens inside each
    /// <see cref="SaveChangesAsync"/> call made before <see cref="IPersistenceTransaction.CommitAsync"/>
    /// — not a separate post-commit step.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown UNCONDITIONALLY when
    /// <c>DbContext.Database.CreateExecutionStrategy().RetriesOnFailure</c> is <see langword="true"/>
    /// — regardless of whether <c>EfCorePersistenceBuilder.WithTransientFaultRetry()</c> was ever
    /// called, since this queries live EF Core execution-strategy state directly (it correctly fires
    /// even when retry was enabled solely via <c>UsePostgreSQL(..., maxRetryCount)</c>). EF Core's
    /// retrying execution strategies require the ENTIRE transactional unit (begin through commit) to
    /// run inside one <c>IExecutionStrategy.ExecuteAsync(...)</c> delegate; this handle-based
    /// begin/commit shape hands control back to arbitrary caller code in between, which is
    /// structurally incompatible with that contract. Use <see cref="ExecuteInTransactionAsync(Func{CancellationToken,Task},CancellationToken)"/>
    /// instead.
    /// </exception>
    public Task<IPersistenceTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        => BeginTransactionAsync(isolationLevel: null, cancellationToken);

    /// <inheritdoc />
    public async Task<IPersistenceTransaction> BeginTransactionAsync(
        IsolationLevel? isolationLevel,
        CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.CreateExecutionStrategy().RetriesOnFailure)
        {
            throw new InvalidOperationException(
                $"'{nameof(BeginTransactionAsync)}' cannot be used when a retrying execution " +
                "strategy is configured (e.g. UsePostgreSQL(..., maxRetryCount:...)). EF Core's " +
                "retrying execution strategies require the entire transactional unit (begin through " +
                $"commit) to run inside one IExecutionStrategy.ExecuteAsync(...) delegate — use " +
                $"'{nameof(ExecuteInTransactionAsync)}' instead.");
        }

        var efTransaction = isolationLevel.HasValue
            ? await _dbContext.Database.BeginTransactionAsync(isolationLevel.Value, cancellationToken)
            : await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        return new EfPersistenceTransaction(
            efTransaction,
            _dbContext.Database.GetDbConnection(),
            (AmbientDbTransactionAccessor?)_ambientTransactionAccessor);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Wraps <c>DbContext.Database.CreateExecutionStrategy().ExecuteAsync(...)</c>,
    /// reusing the same begin/commit machinery as <see cref="BeginTransactionAsync(CancellationToken)"/>/
    /// <see cref="EfPersistenceTransaction.CommitAsync"/>. The whole delegate — including a fresh
    /// <c>BeginTransactionAsync</c> — re-runs on each retry attempt.
    /// </para>
    /// <para>
    /// <strong>Retry double-apply fix:</strong> <see cref="DbContext.ChangeTracker"/>
    /// is cleared at the START of every attempt, including the first. A failed attempt's transaction
    /// rolls back at the database via <c>IDbContextTransaction</c>'s dispose-without-commit
    /// semantics, but anything the FIRST attempt's <paramref name="operation"/> STAGED on this
    /// scoped, REUSED <see cref="DbContext"/> (tracked entities, pending domain events) would
    /// otherwise still be present when the SECOND attempt runs <paramref name="operation"/> again —
    /// producing duplicated inserts, double-counted domain-event dispatch, or stale entity state.
    /// <paramref name="operation"/> MUST be safe to re-run from a clean slate: it should fetch/re-fetch
    /// whatever entities it needs through a repository rather than closing over an entity instance
    /// obtained outside the delegate.
    /// </para>
    /// </remarks>
    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
        => ExecuteInTransactionAsync(operation, isolationLevel: null, verifySucceeded: null, cancellationToken);

    /// <inheritdoc />
    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        IsolationLevel? isolationLevel,
        Func<CancellationToken, Task<bool>>? verifySucceeded,
        CancellationToken cancellationToken = default)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        try
        {
            await ExecuteWithRetryLoggingAsync(() => strategy.ExecuteAsync(cancellationToken, async token =>
            {
                // Clear at the START of every attempt (including the first) so a
                // retry never carries over a previous, failed attempt's staged changes.
                _dbContext.ChangeTracker.Clear();

                var efTransaction = isolationLevel.HasValue
                    ? await _dbContext.Database.BeginTransactionAsync(isolationLevel.Value, token)
                    : await _dbContext.Database.BeginTransactionAsync(token);

                await using var transaction = efTransaction;
                PublishAmbientTransaction(efTransaction);
                try
                {
                    await operation(token);
                    await DomainEventDispatchLoop.RunAsync(_dbContext, _dispatcher, token);
                    await _dbContext.SaveChangesAsync(token);
                    await transaction.CommitAsync(token);
                }
                finally
                {
                    ClearAmbientTransaction();
                }
            }));
        }
        catch (RetryLimitExceededException)
        {
            // Every attempt was exhausted. When the caller supplied a verification hook, give it a
            // chance to confirm the operation actually succeeded server-side before propagating a
            // possibly-false failure. `await` is not permitted in an exception
            // filter, so the check runs in the catch body instead.
            if (verifySucceeded is null || !await verifySucceeded(cancellationToken))
                throw;
        }
    }

    /// <inheritdoc />
    /// <remarks>See the non-generic overload's remarks for the full explanation.</remarks>
    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
        => ExecuteInTransactionAsync(operation, isolationLevel: null, cancellationToken);

    /// <inheritdoc />
    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        IsolationLevel? isolationLevel,
        CancellationToken cancellationToken = default)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        return ExecuteWithRetryLoggingAsync(() => strategy.ExecuteAsync(cancellationToken, async token =>
        {
            // Clear at the START of every attempt — see the non-generic overload.
            _dbContext.ChangeTracker.Clear();

            var efTransaction = isolationLevel.HasValue
                ? await _dbContext.Database.BeginTransactionAsync(isolationLevel.Value, token)
                : await _dbContext.Database.BeginTransactionAsync(token);

            await using var transaction = efTransaction;
            PublishAmbientTransaction(efTransaction);
            try
            {
                var result = await operation(token);
                await DomainEventDispatchLoop.RunAsync(_dbContext, _dispatcher, token);
                await _dbContext.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
                return result;
            }
            finally
            {
                ClearAmbientTransaction();
            }
        }));
    }

    // Publishes/clears the (connection, transaction) pair for the ExecuteInTransactionAsync
    // overloads, which manage their own IDbContextTransaction directly rather than going through
    // EfPersistenceTransaction (that type only wraps the explicit-handle BeginTransactionAsync path).
    private void PublishAmbientTransaction(IDbContextTransaction efTransaction)
    {
        if (_ambientTransactionAccessor is AmbientDbTransactionAccessor accessor)
        {
            accessor.Current =
                (_dbContext.Database.GetDbConnection(), efTransaction.GetDbTransaction());
        }
    }

    private void ClearAmbientTransaction()
    {
        if (_ambientTransactionAccessor is AmbientDbTransactionAccessor accessor)
            accessor.Current = null;
    }

    // Shared retry-exhaustion logging helper — see EfUnitOfWork's class remarks for
    // the full rationale on why RetryLimitExceededException specifically (never a broad
    // `catch (Exception) when RetriesOnFailure`) is the correct signal.
    private async Task<TResult> ExecuteWithRetryLoggingAsync<TResult>(Func<Task<TResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (RetryLimitExceededException ex)
        {
            PersistenceLog.TransientRetryExhausted(_logger, ex, (_retryOptions?.MaxRetryCount ?? 0) + 1);
            throw;
        }
    }

    private async Task ExecuteWithRetryLoggingAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (RetryLimitExceededException ex)
        {
            PersistenceLog.TransientRetryExhausted(_logger, ex, (_retryOptions?.MaxRetryCount ?? 0) + 1);
            throw;
        }
    }
}
#pragma warning restore RS0026
