using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Domain;
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
/// Domain event dispatch fires after <see cref="IPersistenceTransaction.CommitAsync"/> completes
/// successfully, consistent with <c>EfUnitOfWork.SaveChangesAsync</c> semantics.
/// </para>
/// <para>
/// <strong>Hard violation:</strong> Application-layer code must inject
/// <c>ITransactionalUnitOfWork</c> — never <c>IDbContextTransaction</c> directly.
/// </para>
/// </remarks>
public sealed class EfTransactionalUnitOfWork : ITransactionalUnitOfWork
{
    private readonly SharedKernelDbContext _dbContext;
    private readonly IDomainEventDispatcher? _dispatcher;
    private readonly ILogger<EfTransactionalUnitOfWork> _logger;
    private readonly TransientFaultRetryOptions? _retryOptions;

    /// <summary>
    /// Initialises a new <see cref="EfTransactionalUnitOfWork"/>.
    /// </summary>
    /// <param name="dbContext">The scoped shared-kernel DB context.</param>
    /// <param name="dispatcher">
    /// Optional dispatcher for domain events raised during the save cycle.
    /// When <see langword="null"/>, events are cleared but not dispatched.
    /// </param>
    /// <param name="logger">
    /// Optional logger for the <c>TransientRetryExhausted</c> Warning (EventId <c>6008</c>,
    /// WO-053/P-333) — see <see cref="EfUnitOfWork"/>'s remarks for the full rationale, which
    /// applies identically here. Resolved by DI when registered; falls back to
    /// <see cref="NullLogger{T}"/> otherwise.
    /// </param>
    /// <param name="retryOptions">
    /// Optional discoverability options registered by
    /// <c>EfCorePersistenceBuilder.WithTransientFaultRetry(...)</c>, consulted only to compute the
    /// logged <c>AttemptCount</c> value — see <see cref="EfUnitOfWork"/>'s equivalent parameter.
    /// </param>
    public EfTransactionalUnitOfWork(
        SharedKernelDbContext dbContext,
        IDomainEventDispatcher? dispatcher = null,
        ILogger<EfTransactionalUnitOfWork>? logger = null,
        TransientFaultRetryOptions? retryOptions = null)
    {
        _dbContext = dbContext;
        _dispatcher = dispatcher;
        _logger = logger ?? NullLogger<EfTransactionalUnitOfWork>.Instance;
        _retryOptions = retryOptions;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <strong>Dispatch deferral rule (P-105):</strong> When an explicit database transaction is
    /// active (<c>DbContext.Database.CurrentTransaction</c> is non-null), domain event dispatch is
    /// deferred to <c>EfPersistenceTransaction.CommitAsync</c>. Only the raw EF Core save is issued
    /// here — dispatching before the commit would cause duplicate events if the caller commits after.
    /// </para>
    /// <para>
    /// When no transaction is active (<c>CurrentTransaction == null</c>), dispatch fires immediately
    /// after the save, matching <see cref="EfUnitOfWork.SaveChangesAsync"/> semantics exactly.
    /// </para>
    /// </remarks>
    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var result = await ExecuteWithRetryLoggingAsync(() => _dbContext.SaveChangesAsync(ct));

        // Dispatch only when no explicit transaction is active.
        // When CurrentTransaction is non-null, dispatch is deferred to EfPersistenceTransaction.CommitAsync.
        if (_dbContext.Database.CurrentTransaction is null)
            await DispatchAndClearEventsAsync(ct);

        return result;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Opens an EF Core database transaction via <c>DbContext.Database.BeginTransactionAsync</c>
    /// and wraps it in an <see cref="EfPersistenceTransaction"/> adapter that implements
    /// <see cref="IPersistenceTransaction"/>. Domain event dispatch fires when the caller invokes
    /// <c>CommitAsync</c> on the returned transaction handle (not automatically on
    /// <see cref="SaveChangesAsync"/> — within an explicit transaction, save is a staging step).
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown UNCONDITIONALLY (WO-051/P-320) when
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
    public async Task<IPersistenceTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        if (_dbContext.Database.CreateExecutionStrategy().RetriesOnFailure)
        {
            throw new InvalidOperationException(
                $"'{nameof(BeginTransactionAsync)}' cannot be used when a retrying execution " +
                "strategy is configured (e.g. UsePostgreSQL(..., maxRetryCount: ...)). EF Core's " +
                "retrying execution strategies require the entire transactional unit (begin through " +
                $"commit) to run inside one IExecutionStrategy.ExecuteAsync(...) delegate — use " +
                $"'{nameof(ExecuteInTransactionAsync)}' instead.");
        }

        var efTransaction = await _dbContext.Database.BeginTransactionAsync(ct);
        return new EfPersistenceTransaction(efTransaction, this);
    }

    /// <inheritdoc />
    /// <remarks>
    /// WO-051/P-320 — wraps <c>DbContext.Database.CreateExecutionStrategy().ExecuteAsync(...)</c>,
    /// reusing the same begin/commit machinery as <see cref="BeginTransactionAsync"/>/
    /// <see cref="EfPersistenceTransaction.CommitAsync"/> (including the P-105
    /// deferred-domain-event-dispatch-until-commit rule). The whole delegate — including a fresh
    /// <c>BeginTransactionAsync</c> — re-runs on each retry attempt; a failed attempt's transaction
    /// rolls back via <c>IDbContextTransaction</c>'s dispose-without-commit semantics before the
    /// next attempt begins, so no partial/duplicate commit occurs.
    /// </remarks>
    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken ct = default)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        return ExecuteWithRetryLoggingAsync(() => strategy.ExecuteAsync(ct, async token =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(token);
            await operation(token);
            await transaction.CommitAsync(token);
            await DispatchAndClearEventsAsync(token);
        }));
    }

    /// <inheritdoc />
    /// <remarks>See the non-generic overload's remarks for the full explanation (WO-051/P-320).</remarks>
    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken ct = default)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        return ExecuteWithRetryLoggingAsync(() => strategy.ExecuteAsync(ct, async token =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(token);
            var result = await operation(token);
            await transaction.CommitAsync(token);
            await DispatchAndClearEventsAsync(token);
            return result;
        }));
    }

    // WO-053/P-333: shared retry-exhaustion logging helper — see EfUnitOfWork's class remarks for
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

    /// <summary>
    /// Dispatches collected domain events and clears them from all tracked aggregates.
    /// Called after a successful commit (either <see cref="SaveChangesAsync"/> or
    /// <see cref="IPersistenceTransaction.CommitAsync"/>).
    /// </summary>
    internal async Task DispatchAndClearEventsAsync(CancellationToken ct)
    {
        var aggregatesWithEvents = _dbContext.ChangeTracker
            .Entries<IHasDomainEvents>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();

        var allEvents = aggregatesWithEvents
            .SelectMany(e => e.DomainEvents)
            .ToList();

        if (_dispatcher is not null && allEvents.Count > 0)
            await _dispatcher.DispatchAsync(allEvents, ct);

        foreach (var aggregate in aggregatesWithEvents)
            aggregate.ClearDomainEvents();
    }
}
