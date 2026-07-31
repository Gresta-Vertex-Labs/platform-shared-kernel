using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.UnitOfWork;
using SharedKernel.Persistence.EfCore.Context;

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

    /// <summary>
    /// Initialises a new <see cref="EfTransactionalUnitOfWork"/>.
    /// </summary>
    /// <param name="dbContext">The scoped shared-kernel DB context.</param>
    /// <param name="dispatcher">
    /// Optional dispatcher for domain events raised during the save cycle.
    /// When <see langword="null"/>, events are cleared but not dispatched.
    /// </param>
    public EfTransactionalUnitOfWork(
        SharedKernelDbContext dbContext,
        IDomainEventDispatcher? dispatcher = null)
    {
        _dbContext = dbContext;
        _dispatcher = dispatcher;
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
        var result = await _dbContext.SaveChangesAsync(ct);

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
        return new EfTransactionalPersistenceTransaction(efTransaction, this);
    }

    /// <inheritdoc />
    /// <remarks>
    /// WO-051/P-320 — wraps <c>DbContext.Database.CreateExecutionStrategy().ExecuteAsync(...)</c>,
    /// reusing the same begin/commit machinery as <see cref="BeginTransactionAsync"/>/
    /// <see cref="EfTransactionalPersistenceTransaction.CommitAsync"/> (including the P-105
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

        return strategy.ExecuteAsync(ct, async token =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(token);
            await operation(token);
            await transaction.CommitAsync(token);
            await DispatchAndClearEventsAsync(token);
        });
    }

    /// <inheritdoc />
    /// <remarks>See the non-generic overload's remarks for the full explanation (WO-051/P-320).</remarks>
    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken ct = default)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(ct, async token =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(token);
            var result = await operation(token);
            await transaction.CommitAsync(token);
            await DispatchAndClearEventsAsync(token);
            return result;
        });
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

/// <summary>
/// Wraps an <see cref="EfPersistenceTransaction"/> and fires domain event dispatch after
/// <see cref="CommitAsync"/> completes successfully.
/// </summary>
internal sealed class EfTransactionalPersistenceTransaction : IPersistenceTransaction
{
    private readonly Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction _transaction;
    private readonly EfTransactionalUnitOfWork _uow;

    internal EfTransactionalPersistenceTransaction(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        EfTransactionalUnitOfWork uow)
    {
        _transaction = transaction;
        _uow = uow;
    }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _transaction.CommitAsync(ct);
        // Dispatch domain events post-commit, consistent with EfUnitOfWork semantics.
        await _uow.DispatchAndClearEventsAsync(ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Domain events are <strong>not</strong> dispatched on rollback. The change-tracker still
    /// holds any staged events; callers must discard the unit-of-work scope after a rollback to
    /// prevent stale events from being dispatched on a subsequent save.
    /// </remarks>
    public Task RollbackAsync(CancellationToken ct = default)
        => _transaction.RollbackAsync(ct);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
        => _transaction.DisposeAsync();
}
