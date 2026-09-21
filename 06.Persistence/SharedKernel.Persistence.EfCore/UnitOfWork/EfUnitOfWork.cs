using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Application.Transactions;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conventions;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Persistence.EfCore.UnitOfWork;

/// <summary>
/// The unit of work of one specific context. Inject it when a service registers more than one context;
/// otherwise inject the plain <see cref="IUnitOfWork"/>, which resolves to the first registered context.
/// </summary>
/// <typeparam name="TContext">The context this unit of work commits.</typeparam>
/// <remarks>
/// Also resolvable as a keyed <see cref="IUnitOfWork"/> whose key is <c>typeof(TContext)</c>:
/// <c>[FromKeyedServices(typeof(OrderDbContext))] IUnitOfWork unitOfWork</c>.
/// </remarks>
public interface IUnitOfWork<TContext> : IUnitOfWork
    where TContext : SharedKernelDbContext
{
}

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/> for one context — the single commit boundary.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Transactions run inside the execution strategy.</strong> <see cref="ExecuteInTransactionAsync{TResult}(Func{CancellationToken,Task{TResult}},IsolationLevel?,CancellationToken)"/>
/// runs the whole delegate per attempt, so the default Npgsql retry and explicit transactions coexist: a
/// transient failure replays the delegate. An attempt begins the transaction, publishes it on
/// <see cref="IAmbientDbTransaction"/> (Dapper and the audit writer enlist), runs the operation, saves (the
/// context dispatches domain events first), runs the <see cref="OnBeforeCommit"/> callbacks (saving again if
/// they staged changes) and commits. A failed <c>Result</c> or an exception rolls back and clears the change
/// tracker.
/// </para>
/// <para>
/// <strong>Retry safety:</strong> the tracker is cleared before each retried attempt, never before the first.
/// Under a retrying strategy, changes staged before the call would be lost on a retry, so the call refuses to
/// start in that case.
/// </para>
/// </remarks>
#pragma warning disable RS0026 // Mirrors IUnitOfWork's overload set.
internal sealed class EfUnitOfWork<TContext> : IUnitOfWork<TContext>
    where TContext : SharedKernelDbContext
{
    private readonly TContext _dbContext;
    private readonly ILogger _logger;
    private readonly AmbientDbTransactionAccessor? _ambientTransactionAccessor;
    private readonly List<Func<CancellationToken, Task>> _beforeCommit = [];
    private IDbContextTransaction? _transaction;

    public EfUnitOfWork(
        TContext dbContext,
        ILogger<EfUnitOfWork<TContext>>? logger = null,
        IAmbientDbTransaction? ambientTransaction = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _ambientTransactionAccessor = ambientTransaction as AmbientDbTransactionAccessor;
    }

    /// <inheritdoc />
    internal EfUnitOfWork(TContext dbContext, ILogger logger, IAmbientDbTransaction? ambientTransaction)
    {
        _dbContext = dbContext;
        _logger = logger;
        _ambientTransactionAccessor = ambientTransaction as AmbientDbTransactionAccessor;
    }

    /// <inheritdoc />
    public bool IsTransactionActive => _transaction is not null;

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        WithRetryExhaustionLoggingAsync(() => _dbContext.SaveChangesAsync(cancellationToken), ConfiguredAttemptCount);

    /// <inheritdoc />
    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
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

            await _dbContext.SaveChangesAsync(cancellationToken);

            // Indexed loop: a callback may queue another callback.
            for (var i = 0; i < _beforeCommit.Count; i++)
                await _beforeCommit[i](cancellationToken);

            if (_dbContext.ChangeTracker.HasChanges())
                await _dbContext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
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

    private async Task<TResult> JoinAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
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

    private int ConfiguredAttemptCount()
        => (_dbContext.GetService<IDbContextOptions>()
            .FindExtension<PostgreSQLConventionsOptionsExtension>()?.MaxRetryCount ?? 0) + 1;

    // Only RetryLimitExceededException: a concurrency conflict or any other non-transient failure is never
    // retried, so a broader catch would misreport it as a retry exhaustion.
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

/// <summary>Non-generic construction helper for code that holds a context of a statically unknown type (tests, tools).</summary>
internal sealed class EfUnitOfWork
{
    private EfUnitOfWork()
    {
    }

    /// <summary>
    /// Creates the unit of work of <paramref name="context"/>, attaching <paramref name="dispatcher"/> to the
    /// context (domain events are dispatched by the context itself before each save).
    /// </summary>
    public static EfUnitOfWork<TContext> For<TContext>(
        TContext context,
        SharedKernel.Domain.Abstractions.IDomainEventDispatcher? dispatcher = null,
        ILogger? logger = null,
        IAmbientDbTransaction? ambientTransaction = null)
        where TContext : SharedKernelDbContext
    {
        context.AttachLease(context.RequestContext, dispatcher);
        return new EfUnitOfWork<TContext>(context, logger ?? NullLogger.Instance, ambientTransaction);
    }
}
