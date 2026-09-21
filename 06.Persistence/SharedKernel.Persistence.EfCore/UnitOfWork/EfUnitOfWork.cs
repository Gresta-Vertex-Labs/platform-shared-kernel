using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Application.Transactions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conventions;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Persistence.EfCore.UnitOfWork;

/// <summary>
/// EF Core implementation of the shared <see cref="IUnitOfWork"/> — the single commit boundary, always
/// registered by <c>EfCorePersistenceBuilder.Build()</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One implementation.</strong> The former <c>EfUnitOfWork</c>/<c>EfTransactionalUnitOfWork</c>
/// pair and the handle-based <c>BeginTransactionAsync</c> are gone. Every transaction runs through
/// <see cref="ExecuteInTransactionAsync{TResult}(Func{CancellationToken,Task{TResult}},IsolationLevel?,CancellationToken)"/>,
/// inside <c>IExecutionStrategy.ExecuteAsync</c>, so a retrying execution strategy (Npgsql transient
/// fault retry) and explicit transactions coexist: a transient failure replays the whole delegate.
/// </para>
/// <para>
/// <strong>An attempt:</strong> begin the transaction and publish it on <see cref="IAmbientDbTransaction"/>
/// (so a Dapper command service or the audit-trail writer can enlist), run the operation, dispatch
/// domain events and save, run every <see cref="OnBeforeCommit"/> callback (saving again if a callback
/// staged EF changes), commit. A failed <c>Result</c> returned by the operation, or an exception, rolls
/// back and clears the change tracker, so nothing the operation staged can be saved later by accident.
/// </para>
/// <para>
/// <strong>Retry safety:</strong> the change tracker is cleared before every retried attempt (never
/// before the first — changes staged before the call are saved with it). Under a retrying strategy,
/// changes already staged when the call starts would be lost on a retry, so the call refuses to start
/// and throws <see cref="InvalidOperationException"/> instead.
/// </para>
/// <para>
/// <strong>Domain event dispatch</strong> happens before every physical save — see
/// <see cref="DomainEventDispatchLoop"/>. <c>IDomainEventDispatcher</c> is optional; when none is
/// registered events are cleared but not dispatched.
/// </para>
/// <para>
/// <strong>Single constructor rule:</strong> exactly one public constructor, with the optional
/// dependencies as defaulted parameters — a second, shorter constructor could be selected by DI and
/// silently skip the dispatcher.
/// </para>
/// </remarks>
#pragma warning disable RS0026 // Symbol has multiple public overloads with optional parameters — mirrors IUnitOfWork's overload set; see its rationale.
public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly SharedKernelDbContext _dbContext;
    private readonly IDomainEventDispatcher? _dispatcher;
    private readonly ILogger<EfUnitOfWork> _logger;
    private readonly AmbientDbTransactionAccessor? _ambientTransactionAccessor;
    private readonly List<Func<CancellationToken, Task>> _beforeCommit = [];
    private IDbContextTransaction? _transaction;

    /// <summary>Initialises a new <see cref="EfUnitOfWork"/>.</summary>
    /// <param name="dbContext">The scoped shared-kernel DB context.</param>
    /// <param name="dispatcher">Optional domain-event dispatcher; <see langword="null"/> when none is registered.</param>
    /// <param name="logger">Optional logger for <c>TransientRetryExhausted</c> (EventId 6008).</param>
    /// <param name="ambientTransaction">
    /// The scoped <see cref="IAmbientDbTransaction"/> this unit of work publishes its open transaction on.
    /// </param>
    public EfUnitOfWork(
        SharedKernelDbContext dbContext,
        IDomainEventDispatcher? dispatcher = null,
        ILogger<EfUnitOfWork>? logger = null,
        IAmbientDbTransaction? ambientTransaction = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
        _dispatcher = dispatcher;
        _logger = logger ?? NullLogger<EfUnitOfWork>.Instance;
        _ambientTransactionAccessor = ambientTransaction as AmbientDbTransactionAccessor;
    }

    /// <inheritdoc />
    public bool IsTransactionActive => _transaction is not null;

    /// <inheritdoc />
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await DomainEventDispatchLoop.RunAsync(_dbContext, _dispatcher, cancellationToken);
        return await WithRetryExhaustionLoggingAsync(
            () => _dbContext.SaveChangesAsync(cancellationToken),
            ConfiguredAttemptCount);
    }

    /// <inheritdoc />
    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
        => ExecuteInTransactionAsync(operation, isolationLevel: null, cancellationToken);

    /// <inheritdoc />
    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        IsolationLevel? isolationLevel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return ExecuteInTransactionAsync<object?>(
            async token =>
            {
                await operation(token);
                return null;
            },
            isolationLevel,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
        => ExecuteInTransactionAsync(operation, isolationLevel: null, cancellationToken);

    /// <inheritdoc />
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        IsolationLevel? isolationLevel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (IsTransactionActive)
            return await JoinAsync(operation, cancellationToken);

        var strategy = _dbContext.Database.CreateExecutionStrategy();

        if (strategy.RetriesOnFailure && _dbContext.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException(
                "ExecuteInTransactionAsync was called with changes already staged on the DbContext while a " +
                "retrying execution strategy is configured. A retried attempt starts from a cleared change " +
                "tracker, so those changes would be committed on the first attempt but silently lost on a " +
                "retry. Stage every change inside the operation delegate, or call SaveChangesAsync first.");
        }

        var attempt = 0;

        return await WithRetryExhaustionLoggingAsync(() => strategy.ExecuteAsync(
            cancellationToken,
            async token =>
            {
                // A retried attempt must not inherit what the failed one staged (duplicated inserts,
                // double-dispatched events, stale state).
                if (attempt++ > 0)
                    _dbContext.ChangeTracker.Clear();

                return await RunAttemptAsync(operation, isolationLevel, token);
            }),
            () => attempt);
    }

    /// <inheritdoc />
    public void OnBeforeCommit(Func<CancellationToken, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (!IsTransactionActive)
        {
            throw new InvalidOperationException(
                "OnBeforeCommit can only be called while ExecuteInTransactionAsync is running on this unit of work.");
        }

        _beforeCommit.Add(callback);
    }

    private async Task<TResult> RunAttemptAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        IsolationLevel? isolationLevel,
        CancellationToken cancellationToken)
    {
        await using var transaction = isolationLevel.HasValue
            ? await _dbContext.Database.BeginTransactionAsync(isolationLevel.Value, cancellationToken)
            : await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        _transaction = transaction;
        _beforeCommit.Clear();
        PublishAmbientTransaction(transaction);

        try
        {
            var result = await operation(cancellationToken);

            if (result is IHasSuccessFlag { IsSuccess: false })
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();
                return result;
            }

            await DomainEventDispatchLoop.RunAsync(_dbContext, _dispatcher, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Indexed loop: a callback may itself queue another callback.
            for (var i = 0; i < _beforeCommit.Count; i++)
                await _beforeCommit[i](cancellationToken);

            if (_dbContext.ChangeTracker.HasChanges())
            {
                await DomainEventDispatchLoop.RunAsync(_dbContext, _dispatcher, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            // The transaction rolls back when disposed without a commit; nothing staged may survive
            // into a later SaveChanges on this scope.
            _dbContext.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            _transaction = null;
            _beforeCommit.Clear();
            ClearAmbientTransaction();
        }
    }

    // Called inside an active transaction: run, save, and let the outermost call decide the outcome.
    private async Task<TResult> JoinAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        var result = await operation(cancellationToken);

        if (result is not IHasSuccessFlag { IsSuccess: false })
            await SaveChangesAsync(cancellationToken);

        return result;
    }

    private void PublishAmbientTransaction(IDbContextTransaction transaction)
    {
        if (_ambientTransactionAccessor is not null)
            _ambientTransactionAccessor.Current = (_dbContext.Database.GetDbConnection(), transaction.GetDbTransaction());
    }

    private void ClearAmbientTransaction()
    {
        if (_ambientTransactionAccessor is not null)
            _ambientTransactionAccessor.Current = null;
    }

    // Attempts a plain SaveChanges makes before EF Core gives up: the configured retries plus the first try.
    private int ConfiguredAttemptCount()
        => (_dbContext.GetService<IDbContextOptions>()
            .FindExtension<PostgreSQLConventionsOptionsExtension>()?.MaxRetryCount ?? 0) + 1;

    // Catching RetryLimitExceededException specifically — never a broad catch gated on
    // RetriesOnFailure — because a DbUpdateConcurrencyException (or any other non-transient failure)
    // is never retried and never wrapped, so a broader catch would misreport every concurrency
    // conflict as a retry exhaustion.
    private async Task<TResult> WithRetryExhaustionLoggingAsync<TResult>(Func<Task<TResult>> operation, Func<int> attemptCount)
    {
        try
        {
            return await operation();
        }
        catch (RetryLimitExceededException ex)
        {
            PersistenceLog.TransientRetryExhausted(_logger, ex, attemptCount());
            throw;
        }
    }
}
#pragma warning restore RS0026
